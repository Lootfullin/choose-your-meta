using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace RussianMetadata;

public partial class RussianMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RussianMovieProvider> _logger;
    private Plugin Plugin => Plugin.Instance!;

    private const string TmdbApiBase = "https://api.themoviedb.org/3";
    private const string WikidataSparqlEndpoint = "https://query.wikidata.org/sparql";

    [GeneratedRegex(@"\b(tt\d{7,8})\b")]
    private static partial Regex ImdbIdRegex();

    public RussianMovieProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<RussianMovieProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name => "Choose your Meta!";

    public async Task<MetadataResult<Movie>> GetMetadata(
        MovieInfo info,
        CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Movie>();
        string? imdbId = ExtractImdbId(info);

        _logger.LogInformation(
            "RussianMetadata: GetMetadata — Name='{Name}', ExtractedImdbId={ImdbId}",
            info.Name ?? "?", imdbId ?? "N/A");

        if (string.IsNullOrEmpty(imdbId))
        {
            return result;
        }

        result.Item = new Movie();
        result.Item.SetProviderId("Imdb", imdbId);
        result.ResultLanguage = "ru";

        var config = Plugin.Configuration;
        var tmdbApiKey = TmdbApiKeyResolver.Resolve(config);
        if (!string.IsNullOrEmpty(tmdbApiKey))
        {
            await TryTmdbMovie(
                imdbId,
                config,
                tmdbApiKey,
                result,
                cancellationToken);
        }

        // Wikidata is a field-level fallback. For example, TMDB can have a
        // Russian overview but leave the localized title in English.
        await TryWikidata(
            imdbId,
            config,
            result,
            cancellationToken);

        // Keep the lookup name only when neither Russian source has a title.
        // The next metadata provider can still fill every missing field.
        if (string.IsNullOrWhiteSpace(result.Item.Name)
            && !string.IsNullOrWhiteSpace(info.Name))
        {
            result.Item.Name = info.Name;
        }

        result.HasMetadata = true;
        return result;
    }

    private async Task<bool> TryTmdbMovie(
        string imdbId,
        Configuration.PluginConfiguration config,
        string tmdbApiKey,
        MetadataResult<Movie> result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RussianMetadata: Trying TMDB for IMDb ID {ImdbId}", imdbId);

        try
        {
            result.Item ??= new Movie();
            var handler = CreateProxyHandler(config);
            using var httpClient = new HttpClient(handler, disposeHandler: true);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            // Step 1: Find TMDB movie ID by IMDb ID
            var findUrl = $"{TmdbApiBase}/find/{imdbId}?api_key={Uri.EscapeDataString(tmdbApiKey)}&external_source=imdb_id";
            _logger.LogInformation("RussianMetadata: TMDB find URL (without key): {Url}", findUrl.Replace(tmdbApiKey, "***"));
            _logger.LogInformation("RussianMetadata: TMDB find for {ImdbId}", imdbId);

            using var findResponse = await httpClient.GetAsync(findUrl, cancellationToken);
            _logger.LogInformation("RussianMetadata: TMDB find response status: {Status}", (int)findResponse.StatusCode);
            if (!findResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("RussianMetadata: TMDB find failed ({Status})", findResponse.StatusCode);
                return false;
            }

            var findJson = await findResponse.Content.ReadAsStringAsync(cancellationToken);
            var findData = JsonSerializer.Deserialize<TmdbFindResult>(findJson, JsonOptions.Default);
            var movieRef = findData?.MovieResults?.Count > 0 ? findData.MovieResults[0] : null;
            _logger.LogInformation("RussianMetadata: TMDB find result — movieResults={Count}, tmdbId={Id}", findData?.MovieResults?.Count ?? 0, movieRef?.Id.ToString() ?? "N/A");

            if (movieRef == null)
            {
                _logger.LogWarning("RussianMetadata: No TMDB movie for {ImdbId}", imdbId);
                return false;
            }

            // Step 2: Fetch details in Russian
            var detailsUrl = $"{TmdbApiBase}/movie/{movieRef.Id}?api_key={Uri.EscapeDataString(tmdbApiKey)}&language=ru-RU&append_to_response=credits";
            _logger.LogInformation("RussianMetadata: TMDB details URL (without key): {Url}", detailsUrl.Replace(tmdbApiKey, "***"));
            _logger.LogInformation("RussianMetadata: TMDB details for ID {TmdbId}", movieRef.Id);

            using var detailsResponse = await httpClient.GetAsync(detailsUrl, cancellationToken);
            if (!detailsResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("RussianMetadata: TMDB details failed ({Status})", detailsResponse.StatusCode);
                return false;
            }

            var detailsJson = await detailsResponse.Content.ReadAsStringAsync(cancellationToken);
            var movieDetails = JsonSerializer.Deserialize<TmdbMovieDetails>(detailsJson, JsonOptions.Default);
            _logger.LogInformation("RussianMetadata: TMDB details parsed — Title={T}, OriginalTitle={Ot}, OverviewLen={Ol}", movieDetails?.Title ?? "N", movieDetails?.OriginalTitle ?? "N", movieDetails?.Overview?.Length ?? 0);

            if (movieDetails == null) return false;

            var russianTitle = MovieTextLocalization.RussianOrNull(movieDetails.Title);
            if (!string.IsNullOrEmpty(russianTitle) && config.EnableRussianTitles)
            {
                result.Item.Name = russianTitle;
                _logger.LogInformation("RussianMetadata: TMDB — set Russian title: {Title}", russianTitle);
            }

            var russianOverview = MovieTextLocalization.RussianOrNull(movieDetails.Overview);
            if (!string.IsNullOrEmpty(russianOverview) && config.EnableRussianOverviews)
            {
                result.Item.Overview = russianOverview;
                _logger.LogInformation("RussianMetadata: TMDB — set overview ({Len} chars)", russianOverview.Length);
            }

            var russianTagline = MovieTextLocalization.RussianOrNull(movieDetails.Tagline);
            if (!string.IsNullOrEmpty(russianTagline) && config.EnableRussianTaglines)
            {
                result.Item.Tagline = russianTagline;
            }

            if (config.EnableRussianGenres && movieDetails.Genres is { Count: > 0 })
            {
                result.Item.Genres = movieDetails.Genres
                    .Select(genre => genre.Name?.Trim())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .ToArray();
            }

            if (config.EnableRussianStudios && movieDetails.ProductionCompanies is { Count: > 0 })
            {
                var companyIds = movieDetails.ProductionCompanies
                    .Where(company => company.Id > 0)
                    .Select(company => company.Id)
                    .ToArray();
                var russianCompanies = await FetchRussianLabelsByExternalIds(
                    "P11806",
                    companyIds,
                    cancellationToken);
                result.Item.Studios = movieDetails.ProductionCompanies
                    .Select(company => russianCompanies.GetValueOrDefault(company.Id)
                        ?? company.Name?.Trim())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .ToArray();
            }

            if (config.EnableRussianPeople && movieDetails.Credits is not null)
            {
                await AddLocalizedPeople(
                    movieDetails.Credits,
                    result,
                    cancellationToken);
            }

            if (!string.IsNullOrEmpty(movieDetails.OriginalTitle))
            {
                result.Item.OriginalTitle = movieDetails.OriginalTitle;
            }

            result.Item.SetProviderId("Tmdb", movieRef.Id.ToString(CultureInfo.InvariantCulture));

            _logger.LogInformation("RussianMetadata: TMDB success for {ImdbId} -> {Title}", imdbId, movieDetails.Title);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RussianMetadata: TMDB failed for {ImdbId}, will fallback", imdbId);
            return false;
        }
    }

    private async Task<bool> TryWikidata(string imdbId, Configuration.PluginConfiguration config,
        MetadataResult<Movie> result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RussianMetadata: Falling back to Wikidata for {ImdbId}", imdbId);

        try
        {
            var entity = await FetchWikidataEntityByImdbId(imdbId, cancellationToken);
            if (entity == null)
            {
                _logger.LogWarning("RussianMetadata: No Wikidata entity for {ImdbId}", imdbId);
                return false;
            }

            result.Item ??= new Movie();
            bool changed = false;

            bool hasRussianLabel = !string.IsNullOrEmpty(entity.RussianLabel)
                && !entity.RussianLabel.StartsWith("Q", StringComparison.Ordinal);

            if (hasRussianLabel
                && config.EnableRussianTitles
                && string.IsNullOrEmpty(result.Item.Name))
            {
                result.Item.Name = entity.RussianLabel;
                changed = true;
                _logger.LogInformation("RussianMetadata: Wikidata — set Russian title: {Title}", entity.RussianLabel);
            }

            // Only set Russian overview; never overwrite with English (preserve NFO data)
            if (!string.IsNullOrEmpty(entity.RussianDescription)
                && config.EnableRussianOverviews
                && string.IsNullOrEmpty(result.Item.Overview))
            {
                result.Item.Overview = entity.RussianDescription;
                changed = true;
                _logger.LogInformation("RussianMetadata: Wikidata — set RU overview: {Desc}", entity.RussianDescription);
            }

            if (changed)
            {
                _logger.LogInformation("RussianMetadata: Wikidata RU data applied for {ImdbId}", imdbId);
            }
            else
            {
                _logger.LogInformation("RussianMetadata: Wikidata entity found for {ImdbId} but no RU data to apply", imdbId);
            }

            return changed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RussianMetadata: Wikidata error for {ImdbId}", imdbId);
            return false;
        }
    }

    private HttpClientHandler CreateProxyHandler(Configuration.PluginConfiguration config)
    {
        var handler = new HttpClientHandler();

        if (!string.IsNullOrEmpty(config.ProxyUrl))
        {
            var uri = new UriBuilder(config.ProxyUrl);
            if (uri.Scheme == "http" || uri.Scheme == "https")
            {
                var webProxy = new WebProxy(config.ProxyUrl);
                if (!string.IsNullOrEmpty(config.ProxyUsername))
                {
                    webProxy.Credentials = new NetworkCredential(config.ProxyUsername, config.ProxyPassword);
                }
                handler.Proxy = webProxy;
                handler.UseProxy = true;
                _logger.LogInformation("RussianMetadata: Using proxy {Proxy}", config.ProxyUrl);
            }
        }

        return handler;
    }

    /// <summary>
    /// Escape characters unsafe for SPARQL string literals (backslash, double-quote).
    /// Does NOT URL-encode – the SPARQL query is URL-encoded once at the HTTP layer.
    /// </summary>
    private static string EscapeSparqlString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private async Task AddLocalizedPeople(
        TmdbCredits credits,
        MetadataResult<Movie> result,
        CancellationToken cancellationToken)
    {
        var cast = credits.Cast?
            .Where(person => person.Id > 0 && !string.IsNullOrWhiteSpace(person.Name))
            .OrderBy(person => person.Order)
            .Take(50)
            .ToArray() ?? [];
        var crew = credits.Crew?
            .Where(person => person.Id > 0
                && !string.IsNullOrWhiteSpace(person.Name)
                && MovieTextLocalization.MapCrewJob(person.Job) is not null)
            .ToArray() ?? [];
        var ids = cast.Select(person => person.Id)
            .Concat(crew.Select(person => person.Id))
            .Distinct()
            .ToArray();
        var russianNames = await FetchRussianLabelsByExternalIds(
            "P4985",
            ids,
            cancellationToken);

        foreach (var actor in cast)
        {
            var person = new PersonInfo
            {
                Name = russianNames.GetValueOrDefault(actor.Id) ?? actor.Name!.Trim(),
                Role = actor.Character?.Trim() ?? string.Empty,
                Type = PersonKind.Actor,
                SortOrder = actor.Order,
                ImageUrl = BuildProfileUrl(actor.ProfilePath)
            };
            person.SetProviderId("Tmdb", actor.Id.ToString(CultureInfo.InvariantCulture));
            result.AddPerson(person);
        }

        foreach (var member in crew)
        {
            var kind = MovieTextLocalization.MapCrewJob(member.Job);
            if (kind is null)
            {
                continue;
            }

            var person = new PersonInfo
            {
                Name = russianNames.GetValueOrDefault(member.Id) ?? member.Name!.Trim(),
                Role = member.Job?.Trim() ?? string.Empty,
                Type = kind.Value,
                ImageUrl = BuildProfileUrl(member.ProfilePath)
            };
            person.SetProviderId("Tmdb", member.Id.ToString(CultureInfo.InvariantCulture));
            result.AddPerson(person);
        }
    }

    private async Task<Dictionary<int, string>> FetchRussianLabelsByExternalIds(
        string propertyId,
        IReadOnlyCollection<int> ids,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        using var httpClient = _httpClientFactory.CreateClient("RussianMetadata");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "Jellyfin-RussianMetadata/1.3");
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var values = string.Join(
            " ",
            ids.Distinct().Select(id =>
                $"\"{id.ToString(CultureInfo.InvariantCulture)}\""));
        var sparqlQuery = $@"
SELECT ?externalId ?ruLabel WHERE {{
  VALUES ?externalId {{ {values} }}
  ?item wdt:{propertyId} ?externalId.
  ?item rdfs:label ?ruLabel.
  FILTER(LANG(?ruLabel) = ""ru"")
}}";
        var url = $"{WikidataSparqlEndpoint}?format=json&query={Uri.EscapeDataString(sparqlQuery)}";

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<SparqlResult>(
                json,
                JsonOptions.Default);
            return result?.Results?.Bindings?
                .Where(binding =>
                    int.TryParse(
                        binding.ExternalId?.Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out _)
                    && !string.IsNullOrWhiteSpace(binding.RuLabel?.Value))
                .GroupBy(binding => binding.ExternalId!.Value!)
                .ToDictionary(
                    group => int.Parse(
                        group.Key,
                        CultureInfo.InvariantCulture),
                    group => group.First().RuLabel!.Value!,
                    EqualityComparer<int>.Default)
                ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "RussianMetadata: Wikidata label lookup failed for property {PropertyId}",
                propertyId);
            return [];
        }
    }

    private static string? BuildProfileUrl(string? profilePath)
    {
        return string.IsNullOrWhiteSpace(profilePath)
            ? null
            : $"https://image.tmdb.org/t/p/original{profilePath}";
    }

    // ───── IMDb ID extraction ─────

    private string? ExtractImdbId(MovieInfo info)
    {
        if (info.ProviderIds.TryGetValue("Imdb", out var id))
            return id;

        if (!string.IsNullOrEmpty(info.Path))
        {
            var match = ImdbIdRegex().Match(info.Path);
            if (match.Success)
                return match.Groups[1].Value;
        }

        if (!string.IsNullOrEmpty(info.Name))
        {
            var match = ImdbIdRegex().Match(info.Name);
            if (match.Success)
                return match.Groups[1].Value;
        }

        return null;
    }

    // ───── Wikidata ─────

    private async Task<WikidataEntity?> FetchWikidataEntityByImdbId(string imdbId, CancellationToken cancellationToken)
    {
        using var httpClient = _httpClientFactory.CreateClient("RussianMetadata");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Jellyfin-RussianMetadata/1.0");
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        // Fetch both RU and EN labels/descriptions in one query
        var safeImdbId = EscapeSparqlString(imdbId);
        var sparqlQuery = $@"
SELECT ?item ?ruLabel ?enLabel ?ruDescription ?enDescription WHERE {{
  ?item wdt:P345 ""{safeImdbId}"".
  OPTIONAL {{ ?item rdfs:label ?ruLabel. FILTER(LANG(?ruLabel) = ""ru"") }}
  OPTIONAL {{ ?item rdfs:label ?enLabel. FILTER(LANG(?enLabel) = ""en"") }}
  OPTIONAL {{ ?item schema:description ?ruDescription. FILTER(LANG(?ruDescription) = ""ru"") }}
  OPTIONAL {{ ?item schema:description ?enDescription. FILTER(LANG(?enDescription) = ""en"") }}
}}
LIMIT 1";
        var url = $"{WikidataSparqlEndpoint}?format=json&query={Uri.EscapeDataString(sparqlQuery)}";

        _logger.LogInformation("RussianMetadata: Wikidata query for {ImdbId}", imdbId);

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var sparqlResult = JsonSerializer.Deserialize<SparqlResult>(json, JsonOptions.Default);
            var binding = sparqlResult?.Results?.Bindings?.Count > 0 ? sparqlResult.Results.Bindings[0] : null;

            if (binding == null) return null;

            return new WikidataEntity
            {
                EntityId = binding.Item?.Value,
                RussianLabel = binding.RuLabel?.Value ?? binding.ItemLabel?.Value,
                EnglishLabel = binding.EnLabel?.Value,
                RussianDescription = binding.RuDescription?.Value,
                EnglishDescription = binding.EnDescription?.Value
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RussianMetadata: Wikidata fetch error for {ImdbId}", imdbId);
            return null;
        }
    }

    // ───── Search ─────

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(MovieInfo searchInfo, CancellationToken cancellationToken)
    {
        var results = new List<RemoteSearchResult>();

        var config = Plugin.Configuration;
        var tmdbApiKey = TmdbApiKeyResolver.Resolve(config);

        // Try TMDB search first
        if (!string.IsNullOrEmpty(tmdbApiKey))
        {
            try
            {
                var handler = CreateProxyHandler(config);
                using var httpClient = new HttpClient(handler, disposeHandler: true);
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                var query = Uri.EscapeDataString(searchInfo.Name);
                var searchUrl = $"{TmdbApiBase}/search/movie?api_key={Uri.EscapeDataString(tmdbApiKey)}&language=ru-RU&query={query}";

                using var response = await httpClient.GetAsync(searchUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var searchResult = JsonSerializer.Deserialize<TmdbSearchResponse>(json, JsonOptions.Default);
                    if (searchResult?.Results != null)
                    {
                        foreach (var m in searchResult.Results)
                        {
                            var sr = new RemoteSearchResult
                            {
                                Name = m.Title ?? m.OriginalTitle ?? "Unknown",
                                Overview = m.Overview,
                                SearchProviderName = Name,
                                ProductionYear = m.ReleaseDate?.Length >= 4
                                    && int.TryParse(m.ReleaseDate[..4], out var yr) ? yr : null
                            };
                            sr.SetProviderId("Tmdb", m.Id.ToString(CultureInfo.InvariantCulture));
                            results.Add(sr);
                        }
                        return results;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "RussianMetadata: TMDB search failed, fallback to Wikidata");
            }
        }

        // Fallback: Wikidata search (existing logic)
        try
        {
            using var httpClient = _httpClientFactory.CreateClient("RussianMetadata");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Jellyfin-RussianMetadata/1.0");
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var safeName = EscapeSparqlString(searchInfo.Name);
            var sparqlQuery = $@"
SELECT ?item ?itemLabel ?description WHERE {{
  ?item wdt:P31 wd:Q11424.
  ?item rdfs:label ?itemLabel.
  FILTER(LANG(?itemLabel) = ""ru"")
  FILTER(CONTAINS(LCASE(?itemLabel), LCASE(""{safeName}"")))
}}
LIMIT 10";
            var url = $"{WikidataSparqlEndpoint}?format=json&query={Uri.EscapeDataString(sparqlQuery)}";

            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var sparqlResult = JsonSerializer.Deserialize<SparqlResult>(json, JsonOptions.Default);
                if (sparqlResult?.Results?.Bindings != null)
                {
                    foreach (var binding in sparqlResult.Results.Bindings)
                    {
                        var sr = new RemoteSearchResult
                        {
                            Name = binding.ItemLabel?.Value ?? "Unknown",
                            Overview = binding.Description?.Value,
                            SearchProviderName = Name,
                        };
                        results.Add(sr);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RussianMetadata: Wikidata search error");
        }

        return results;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("RussianMetadata");
        return httpClient.GetAsync(url, cancellationToken);
    }
}

// ───── TMDB JSON models ─────

internal class TmdbFindResult
{
    [JsonPropertyName("movie_results")]
    public List<TmdbMovieRef>? MovieResults { get; set; }
    [JsonPropertyName("tv_results")]
    public List<TmdbTvRef>? TvResults { get; set; }
}

internal class TmdbMovieRef
{
    public int Id { get; set; }
}

internal class TmdbTvRef
{
    public int Id { get; set; }
}

internal class TmdbMovieDetails
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Overview { get; set; }
    public string? Tagline { get; set; }
    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }
    public List<TmdbGenre>? Genres { get; set; }
    [JsonPropertyName("production_companies")]
    public List<TmdbCompany>? ProductionCompanies { get; set; }
    public TmdbCredits? Credits { get; set; }
}

internal class TmdbGenre
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

internal class TmdbCompany
{
    public int Id { get; set; }
    public string? Name { get; set; }
}

internal class TmdbCredits
{
    public List<TmdbCastMember>? Cast { get; set; }
    public List<TmdbCrewMember>? Crew { get; set; }
}

internal class TmdbCastMember
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Character { get; set; }
    public int Order { get; set; }
    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }
}

internal class TmdbCrewMember
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Job { get; set; }
    [JsonPropertyName("profile_path")]
    public string? ProfilePath { get; set; }
}

internal class TmdbSearchResponse
{
    public List<TmdbSearchItem>? Results { get; set; }
}

internal class TmdbSearchItem
{
    public int Id { get; set; }
    public string? Title { get; set; }
    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }
    public string? Overview { get; set; }
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }
}

// ───── Wikidata / SPARQL models ─────

internal class WikidataEntity
{
    public string? EntityId { get; set; }
    public string? RussianLabel { get; set; }
    public string? EnglishLabel { get; set; }
    public string? RussianDescription { get; set; }
    public string? EnglishDescription { get; set; }
}

internal class SparqlResult
{
    public SparqlHead? Head { get; set; }
    public SparqlResults? Results { get; set; }
}

internal class SparqlHead
{
    public List<string>? Vars { get; set; }
}

internal class SparqlResults
{
    public List<SparqlBinding>? Bindings { get; set; }
}

internal class SparqlBinding
{
    public SparqlValue? Item { get; set; }
    public SparqlValue? ItemLabel { get; set; }
    public SparqlValue? Description { get; set; }
    // Extended Wikidata binding with separate RU/EN fields
    public SparqlValue? RuLabel { get; set; }
    public SparqlValue? EnLabel { get; set; }
    public SparqlValue? RuDescription { get; set; }
    public SparqlValue? EnDescription { get; set; }
    public SparqlValue? ExternalId { get; set; }
    // IMDb ID in search results
    [JsonPropertyName("imdbId")]
    public SparqlValue? ImdbId { get; set; }
}

internal class SparqlValue
{
    public string? Type { get; set; }
    public string? Value { get; set; }
    [JsonPropertyName("xml:lang")]
    public string? Lang { get; set; }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNameCaseInsensitive = true
    };
}
