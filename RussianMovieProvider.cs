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
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace RussianMetadata;

public partial class RussianMovieProvider : IRemoteMetadataProvider<Movie, MovieInfo>, ICustomMetadataProvider<Movie>
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

    public string Name => "Russian Metadata";

    public Task<MetadataResult<Movie>> GetMetadata(MovieInfo info, CancellationToken cancellationToken)
    {
        // Do NOT create a new Movie() here — Jellyfin may interpret a non-null
        // Item with HasMetadata=false as a valid result and overwrite existing data.
        // IMDb ID extraction and all other work is done in FetchAsync.
        var result = new MetadataResult<Movie>();

        _logger.LogInformation(
            "RussianMetadata: GetMetadata — Name='{Name}'",
            info.Name ?? "?");

        return Task.FromResult(result);
    }

    private async Task<bool> TryTmdbMovie(string imdbId, Configuration.PluginConfiguration config,
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
            var findUrl = $"{TmdbApiBase}/find/{imdbId}?api_key={config.TmdbApiKey}&external_source=imdb_id";
            _logger.LogInformation("RussianMetadata: TMDB find URL (without key): {Url}", findUrl.Replace(config.TmdbApiKey, "***"));
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
            var detailsUrl = $"{TmdbApiBase}/movie/{movieRef.Id}?api_key={config.TmdbApiKey}&language=ru-RU";
            _logger.LogInformation("RussianMetadata: TMDB details URL (without key): {Url}", detailsUrl.Replace(config.TmdbApiKey, "***"));
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

            if (!string.IsNullOrEmpty(movieDetails.Title) && config.EnableRussianTitles)
            {
                result.Item.Name = movieDetails.Title;
                _logger.LogInformation("RussianMetadata: TMDB — set title: {Title}", movieDetails.Title);
            }

            if (!string.IsNullOrEmpty(movieDetails.Overview) && config.EnableRussianOverviews)
            {
                result.Item.Overview = movieDetails.Overview;
                _logger.LogInformation("RussianMetadata: TMDB — set overview ({Len} chars)", movieDetails.Overview.Length);
            }

            if (!string.IsNullOrEmpty(movieDetails.OriginalTitle))
            {
                result.Item.OriginalTitle = movieDetails.OriginalTitle;
            }

            result.Item.SetProviderId("Tmdb", movieRef.Id.ToString(CultureInfo.InvariantCulture));

            _logger.LogInformation("RussianMetadata: TMDB success for {ImdbId} -> {Title}", imdbId, movieDetails.Title);
            return true;
        }
        catch (Exception ex)
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

            if (hasRussianLabel && config.EnableRussianTitles)
            {
                result.Item.Name = entity.RussianLabel;
                changed = true;
                _logger.LogInformation("RussianMetadata: Wikidata — set Russian title: {Title}", entity.RussianLabel);
            }

            // Only set Russian overview; never overwrite with English (preserve NFO data)
            if (!string.IsNullOrEmpty(entity.RussianDescription) && config.EnableRussianOverviews)
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
        catch (Exception ex)
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

        // ───── ICustomMetadataProvider: runs AFTER all remote providers to apply Russian data ─────

    public async Task<ItemUpdateType> FetchAsync(Movie item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        if (!item.TryGetProviderId("Imdb", out var imdbId) || string.IsNullOrEmpty(imdbId))
        {
            _logger.LogInformation("RussianMetadata: No IMDb ID for movie {Name}, skipping custom fetch", item.Name);
            return ItemUpdateType.None;
        }

        _logger.LogInformation("RussianMetadata: Custom fetch for movie {Name} (Imdb={ImdbId})", item.Name, imdbId);

        var config = Plugin.Configuration;
        var tempResult = new MetadataResult<Movie>();

        bool success = false;
        if (!string.IsNullOrEmpty(config.TmdbApiKey))
        {
            _logger.LogInformation("RussianMetadata: Custom fetch — trying TMDB for {ImdbId}", imdbId);
            success = await TryTmdbMovie(imdbId, config, tempResult, cancellationToken);
        }

        if (!success)
        {
            _logger.LogInformation("RussianMetadata: Custom fetch — trying Wikidata for {ImdbId}", imdbId);
            success = await TryWikidata(imdbId, config, tempResult, cancellationToken);
        }

        if (success && tempResult.Item != null)
        {
            if (config.EnableRussianTitles && !string.IsNullOrEmpty(tempResult.Item.Name))
            {
                item.Name = tempResult.Item.Name;
                _logger.LogInformation("RussianMetadata: Custom fetch — set name to {Name}", item.Name);
            }

            if (config.EnableRussianOverviews && !string.IsNullOrEmpty(tempResult.Item.Overview))
            {
                item.Overview = tempResult.Item.Overview;
                _logger.LogInformation("RussianMetadata: Custom fetch — set overview ({Len} chars)", item.Overview.Length);
            }

            if (!string.IsNullOrEmpty(tempResult.Item.OriginalTitle))
            {
                item.OriginalTitle = tempResult.Item.OriginalTitle;
            }

            return ItemUpdateType.MetadataEdit;
        }

        _logger.LogInformation("RussianMetadata: Custom fetch — no Russian data for {Name}", item.Name);
        return ItemUpdateType.None;
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
        var handler = new HttpClientHandler();
        using var httpClient = new HttpClient(handler, disposeHandler: true);
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Jellyfin-RussianMetadata/1.0");
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        // Fetch both RU and EN labels/descriptions in one query
        var sparqlQuery = $@"
SELECT ?item ?ruLabel ?enLabel ?ruDescription ?enDescription WHERE {{
  ?item wdt:P345 ""{imdbId}"".
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
        catch (Exception ex)
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

        // Try TMDB search first
        if (!string.IsNullOrEmpty(config.TmdbApiKey))
        {
            try
            {
                var handler = CreateProxyHandler(config);
                using var httpClient = new HttpClient(handler, disposeHandler: true);
                httpClient.Timeout = TimeSpan.FromSeconds(5);

                var query = Uri.EscapeDataString(searchInfo.Name);
                var searchUrl = $"{TmdbApiBase}/search/movie?api_key={config.TmdbApiKey}&language=ru-RU&query={query}";

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
            catch (Exception ex)
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

            var query = Uri.EscapeDataString(searchInfo.Name);
            var sparqlQuery = $@"
SELECT ?item ?itemLabel ?description WHERE {{
  ?item wdt:P31 wd:Q11424.
  ?item rdfs:label ?itemLabel.
  FILTER(LANG(?itemLabel) = ""ru"")
  FILTER(CONTAINS(LCASE(?itemLabel), LCASE(""{query}"")))
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
        catch (Exception ex)
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
    [JsonPropertyName("original_title")]
    public string? OriginalTitle { get; set; }
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }
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
