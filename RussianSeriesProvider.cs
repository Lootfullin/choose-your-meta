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
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace RussianMetadata;

public partial class RussianSeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, ICustomMetadataProvider<Series>
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RussianSeriesProvider> _logger;
    private Plugin Plugin => Plugin.Instance!;

    private const string TmdbApiBase = "https://api.themoviedb.org/3";
    private const string WikidataSparqlEndpoint = "https://query.wikidata.org/sparql";

    [GeneratedRegex(@"\b(tt\d{7,8})\b")]
    private static partial Regex ImdbIdRegex();

    public RussianSeriesProvider(
        IHttpClientFactory httpClientFactory,
        ILogger<RussianSeriesProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public string Name => "Choose your Meta!";

    public Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<Series>();

        string? imdbId = ExtractImdbId(info);

        _logger.LogInformation(
            "RussianMetadata (Series): GetMetadata — Name='{Name}', Path='{Path}', ExtractedImdbId={ImdbId}",
            info.Name, info.Path, imdbId ?? "N/A");

        // Pass IMDb ID to Jellyfin so subsequent FetchAsync can find it in ProviderIds.
        // Only set HasMetadata=true when we have something to contribute.
        if (!string.IsNullOrEmpty(imdbId))
        {
            result.HasMetadata = true;
            result.Item = new Series();
            result.Item.SetProviderId("Imdb", imdbId);

            // Preserve the original name so Jellyfin doesn't lose the item title.
            if (!string.IsNullOrEmpty(info.Name))
            {
                result.Item.Name = info.Name;
            }
        }

        return Task.FromResult(result);
    }

    // ───── ICustomMetadataProvider: runs AFTER all remote providers to apply Russian data ─────

    public async Task<ItemUpdateType> FetchAsync(Series item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        // Try to get IMDb ID from provider IDs (e.g., from NFO or other metadata provider)
        if (!item.TryGetProviderId("Imdb", out var imdbId) || string.IsNullOrEmpty(imdbId))
        {
            _logger.LogInformation("RussianMetadata (Series): No IMDb ID in ProviderIds for {Name}, trying path", item.Name);
        }

        // Fallback: extract IMDb ID from series folder path (e.g., ".../tt32603540 Landyshi.../")
        if (string.IsNullOrEmpty(imdbId) && !string.IsNullOrEmpty(item.Path))
        {
            var match = ImdbIdRegex().Match(item.Path);
            if (match.Success)
            {
                imdbId = match.Groups[1].Value;
                _logger.LogInformation("RussianMetadata (Series): Extracted IMDb ID {ImdbId} from path '{Path}'", imdbId, item.Path);
            }
        }

        // Derive series name from the actual folder path rather than item.Name,
        // which may incorrectly contain the parent/root library folder name.
        var config = Plugin.Configuration;
        var seriesName = item.Name;
        if (!string.IsNullOrEmpty(item.Path))
        {
            var folderName = GetFolderNameFromPath(item.Path);
            if (!string.IsNullOrEmpty(folderName))
            {
                seriesName = CleanSeriesName(folderName);
                if (seriesName != item.Name)
                {
                    _logger.LogInformation("RussianMetadata (Series): Using path-derived name '{DerivedName}' instead of item.Name '{ItemName}'", seriesName, item.Name);
                }
            }
        }

        // If no IMDb ID, try finding one by name (using the path-derived series name)
        if (string.IsNullOrEmpty(imdbId) && !string.IsNullOrEmpty(seriesName))
        {
            try { imdbId = await FindImdbIdBySeriesName(seriesName, cancellationToken); }
            catch { }
            _logger.LogInformation("RussianMetadata (Series): FetchAsync FindImdbIdBySeriesName — ImdbId={Id}", imdbId ?? "N/A");
        }

        var tempResult = new MetadataResult<Series>();
        bool tmdbSuccess = false;
        bool wikidataSuccess = false;

        // Step 1: Try TMDB
        if (!string.IsNullOrEmpty(config.TmdbApiKey))
        {
            _logger.LogInformation("RussianMetadata (Series): FetchAsync Step 1 — Trying TMDB for {Name}", seriesName);
            tmdbSuccess = await TryTmdbSeries(seriesName, imdbId, config, tempResult, cancellationToken);
        }

        // Step 2: Wikidata by IMDb ID
        if (!tmdbSuccess && !string.IsNullOrEmpty(imdbId))
        {
            _logger.LogInformation("RussianMetadata (Series): FetchAsync Step 2 — Wikidata by ID {ImdbId}", imdbId);
            wikidataSuccess = await TryWikidata(imdbId, config, tempResult, cancellationToken);
        }

        // Step 3: Wikidata by name (fallback when no IMDb ID available)
        if (!tmdbSuccess && !wikidataSuccess && string.IsNullOrEmpty(imdbId) && !string.IsNullOrEmpty(seriesName))
        {
            _logger.LogInformation("RussianMetadata (Series): FetchAsync Step 3 — Wikidata by name '{Name}'", seriesName);
            wikidataSuccess = await TryWikidataByName(seriesName, config, tempResult, cancellationToken);
        }

        if ((tmdbSuccess || wikidataSuccess) && tempResult.Item != null)
        {
            if (config.EnableRussianTitles && !string.IsNullOrEmpty(tempResult.Item.Name))
            {
                item.Name = tempResult.Item.Name;
                _logger.LogInformation("RussianMetadata (Series): FetchAsync — set name to {Name}", item.Name);
            }

            if (config.EnableRussianOverviews && !string.IsNullOrEmpty(tempResult.Item.Overview))
            {
                item.Overview = tempResult.Item.Overview;
                _logger.LogInformation("RussianMetadata (Series): FetchAsync — set overview ({Len} chars)", item.Overview.Length);
            }

            return ItemUpdateType.MetadataEdit;
        }

        _logger.LogInformation("RussianMetadata (Series): FetchAsync — no Russian data for {Name}", item.Name);
        return ItemUpdateType.None;
    }

    private async Task<bool> TryTmdbSeries(string name, string? imdbId, Configuration.PluginConfiguration config,
        MetadataResult<Series> result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RussianMetadata (Series): Trying TMDB for {Name}", name);

        try
        {
            var handler = CreateProxyHandler(config);
            using var httpClient = new HttpClient(handler, disposeHandler: true);
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            int? tmdbId = null;

            // Path A: Find by IMDb ID
            if (!string.IsNullOrEmpty(imdbId))
            {
                var findUrl = $"{TmdbApiBase}/find/{imdbId}?api_key={Uri.EscapeDataString(config.TmdbApiKey)}&external_source=imdb_id";
                _logger.LogInformation("RussianMetadata (Series): TMDB find URL (without key): {Url}", findUrl.Replace(config.TmdbApiKey, "***"));
                _logger.LogInformation("RussianMetadata (Series): TMDB find for {ImdbId}", imdbId);

                using var findResponse = await httpClient.GetAsync(findUrl, cancellationToken);
                _logger.LogInformation("RussianMetadata (Series): TMDB find response status: {Status}", (int)findResponse.StatusCode);
                if (findResponse.IsSuccessStatusCode)
                {
                    var findJson = await findResponse.Content.ReadAsStringAsync(cancellationToken);
                    var findData = JsonSerializer.Deserialize<TmdbFindResult>(findJson, JsonOptions.Default);
                    var tvRef = findData?.TvResults?.Count > 0 ? findData.TvResults[0] : null;
                    tmdbId = tvRef?.Id;
                    _logger.LogInformation("RussianMetadata (Series): TMDB find result — tvResults={Count}, tmdbId={Id}", findData?.TvResults?.Count ?? 0, tmdbId?.ToString() ?? "N/A");
                }
                else
                {
                    _logger.LogWarning("RussianMetadata (Series): TMDB find failed ({Status})", findResponse.StatusCode);
                }
            }

            // Path B: No IMDb ID or not found by IMDb — search by name
            if (tmdbId == null && !string.IsNullOrEmpty(name))
            {
                var query = Uri.EscapeDataString(name);
                var searchUrl = $"{TmdbApiBase}/search/tv?api_key={Uri.EscapeDataString(config.TmdbApiKey)}&language=ru-RU&query={query}";
                _logger.LogInformation("RussianMetadata (Series): TMDB search URL (without key): {Url}", searchUrl.Replace(config.TmdbApiKey, "***"));
                _logger.LogInformation("RussianMetadata (Series): TMDB search by name: {Name}", name);

                using var searchResponse = await httpClient.GetAsync(searchUrl, cancellationToken);
                _logger.LogInformation("RussianMetadata (Series): TMDB search response status: {Status}", (int)searchResponse.StatusCode);
                if (searchResponse.IsSuccessStatusCode)
                {
                    var searchJson = await searchResponse.Content.ReadAsStringAsync(cancellationToken);
                    var searchResult = JsonSerializer.Deserialize<SeriesTmdbTvSearchResponse>(searchJson, JsonOptions.Default);
                    if (searchResult?.Results?.Count > 0)
                    {
                        tmdbId = searchResult.Results[0].Id;
                        _logger.LogInformation("RussianMetadata (Series): TMDB search — {Name} matched {Count} results, picked first ID={Id}", name, searchResult.Results.Count, tmdbId);
                    }
                    else
                    {
                        _logger.LogWarning("RussianMetadata (Series): TMDB search by name returned 0 results");
                    }
                }
                else
                {
                    _logger.LogWarning("RussianMetadata (Series): TMDB search failed ({Status})", searchResponse.StatusCode);
                }
            }

            if (tmdbId == null)
            {
                _logger.LogWarning("RussianMetadata (Series): No TMDB TV entry for {Name}", name);
                return false;
            }

            // Step 2: Fetch details in Russian
            var detailsUrl = $"{TmdbApiBase}/tv/{tmdbId}?api_key={Uri.EscapeDataString(config.TmdbApiKey)}&language=ru-RU";
            _logger.LogInformation("RussianMetadata (Series): TMDB details URL (without key): {Url}", detailsUrl.Replace(config.TmdbApiKey, "***"));
            _logger.LogInformation("RussianMetadata (Series): TMDB details for ID {TmdbId}", tmdbId);

            using var detailsResponse = await httpClient.GetAsync(detailsUrl, cancellationToken);
            _logger.LogInformation("RussianMetadata (Series): TMDB details response status: {Status}", (int)detailsResponse.StatusCode);
            if (!detailsResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("RussianMetadata (Series): TMDB details failed ({Status})", detailsResponse.StatusCode);
                return false;
            }

            var detailsJson = await detailsResponse.Content.ReadAsStringAsync(cancellationToken);
            var tvDetails = JsonSerializer.Deserialize<SeriesTmdbTvDetails>(detailsJson, JsonOptions.Default);
            _logger.LogInformation("RussianMetadata (Series): TMDB details parsed — Name={N}, OriginalName={On}, OverviewLen={Ol}", tvDetails?.Name ?? "N", tvDetails?.OriginalName ?? "N", tvDetails?.Overview?.Length ?? 0);

            if (tvDetails == null) return false;

            // Initialize result item (may be null if no IMDb ID was available)
            result.HasMetadata = true;
            result.Item ??= new Series();

            // TMDB runs first now, so always apply its data
            if (!string.IsNullOrEmpty(tvDetails.Name) && config.EnableRussianTitles)
            {
                result.Item.Name = tvDetails.Name;
                _logger.LogInformation("RussianMetadata (Series): TMDB — set name: {Name}", tvDetails.Name);
            }

            if (!string.IsNullOrEmpty(tvDetails.Overview) && config.EnableRussianOverviews)
            {
                result.Item.Overview = tvDetails.Overview;
                _logger.LogInformation("RussianMetadata (Series): TMDB — set overview ({Len} chars)", tvDetails.Overview.Length);
            }

            if (!string.IsNullOrEmpty(tvDetails.OriginalName))
            {
                result.Item.OriginalTitle = tvDetails.OriginalName;
            }

            result.Item.SetProviderId("Tmdb", tmdbId.Value.ToString(CultureInfo.InvariantCulture));

            _logger.LogInformation("RussianMetadata (Series): TMDB success for {Name} -> {Title}", name, tvDetails.Name);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RussianMetadata (Series): TMDB failed for {Name}, will fallback", name);
            return false;
        }
    }

    private async Task<bool> TryWikidata(string imdbId, Configuration.PluginConfiguration config,
        MetadataResult<Series> result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RussianMetadata (Series): Wikidata lookup for {ImdbId}", imdbId);

        try
        {
            var entity = await FetchWikidataEntityByImdbId(imdbId, cancellationToken);
            if (entity == null)
            {
                _logger.LogWarning("RussianMetadata (Series): No Wikidata entity for {ImdbId}", imdbId);
                return false;
            }

            result.Item ??= new Series();
            bool changed = false;

            // Try Russian label first
            bool hasRussianLabel = !string.IsNullOrEmpty(entity.RussianLabel)
                && !entity.RussianLabel.StartsWith("Q", StringComparison.Ordinal);

            if (hasRussianLabel && config.EnableRussianTitles)
            {
                result.Item.Name = entity.RussianLabel;
                changed = true;
                _logger.LogInformation("RussianMetadata (Series): Wikidata — set Russian name: {Name}", entity.RussianLabel);
            }

            // Only set Russian overview; never English (preserve NFO data)
            if (!string.IsNullOrEmpty(entity.RussianDescription) && config.EnableRussianOverviews)
            {
                result.Item.Overview = entity.RussianDescription;
                changed = true;
                _logger.LogInformation("RussianMetadata (Series): Wikidata — set RU overview: {Desc}", entity.RussianDescription);
            }

            // Return true only if we applied RUSSIAN data
            // For English-only data, return false to preserve NFO/other provider data
            if (changed)
            {
                _logger.LogInformation("RussianMetadata (Series): Wikidata RU data applied for {ImdbId}", imdbId);
            }
            else
            {
                _logger.LogInformation("RussianMetadata (Series): Wikidata entity found for {ImdbId} but no RU data to apply (keeping NFO/other data)", imdbId);
            }

            return changed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RussianMetadata (Series): Wikidata error for {ImdbId}", imdbId);
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
                _logger.LogInformation("RussianMetadata (Series): Using proxy {Proxy}", config.ProxyUrl);
            }
        }

        return handler;
    }

    // ───── IMDb ID extraction ─────

    private string? ExtractImdbId(SeriesInfo info)
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

    /// <summary>
    /// Escape characters unsafe for SPARQL string literals (backslash, double-quote).
    /// Does NOT URL-encode – the SPARQL query is URL-encoded once at the HTTP layer.
    /// </summary>
    private static string EscapeSparqlString(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    /// <summary>
    /// Strip "ttXXXXXX " prefix from series folder names like "tt9169516 Run Away" -> "Run Away"
    /// </summary>
    private static string CleanSeriesName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return name ?? string.Empty;
        // Remove "ttXXXXXXXX " prefix
        var cleaned = ImdbIdRegex().Replace(name, "").TrimStart();
        return string.IsNullOrWhiteSpace(cleaned) ? name : cleaned;
    }

    /// <summary>
    /// Extract the last directory/folder name from a file path.
    /// Works on both Windows (\\) and Unix (/) paths.
    /// e.g., "/data/tvshows/Сериалы бабушки/tt32603540 Landyshi. Takaya nezhnaya lyubov/"
    ///       -> "tt32603540 Landyshi. Takaya nezhnaya lyubov"
    /// </summary>
    private static string GetFolderNameFromPath(string path)
    {
        var trimmed = path.Replace('\\', '/').TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }

    /// <summary>
    /// Search Wikidata by series name to find the IMDb ID (fallback when no tt ID in filename)
    /// </summary>
    private async Task<string?> FindImdbIdBySeriesName(string seriesName, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RussianMetadata (Series): Searching IMDb ID by name: {Name}", seriesName);

        try
        {
            using var httpClient = _httpClientFactory.CreateClient("RussianMetadata");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Jellyfin-RussianMetadata/1.0");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            // First, try to find the item by exact name match (any language)
            var cleanName = CleanSeriesName(seriesName);
            var safeName = EscapeSparqlString(cleanName);

            var sparqlQuery = $@"
SELECT ?item ?imdbId WHERE {{
  ?item wdt:P31/wdt:P279* wd:Q15416.
  ?item rdfs:label ?label.
  FILTER(LCASE(?label) = LCASE(""{safeName}""))
  ?item wdt:P345 ?imdbId.
}}
LIMIT 1";

            var url = $"{WikidataSparqlEndpoint}?format=json&query={Uri.EscapeDataString(sparqlQuery)}";

            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var sparqlResult = JsonSerializer.Deserialize<SparqlResult>(json, JsonOptions.Default);
                var binding = sparqlResult?.Results?.Bindings?.Count > 0 ? sparqlResult.Results.Bindings[0] : null;

                if (binding?.ImdbId?.Value != null)
                {
                    _logger.LogInformation("RussianMetadata (Series): Found IMDb ID {ImdbId} by name '{CleanName}'", binding.ImdbId.Value, cleanName);
                    return binding.ImdbId.Value;
                }
            }

            // Retry with CONTAINS for fuzzy search (any language)
            var sparqlQueryFuzzy = $@"
SELECT ?item ?imdbId ?label WHERE {{
  ?item wdt:P31/wdt:P279* wd:Q15416.
  ?item rdfs:label ?label.
  FILTER(CONTAINS(LCASE(?label), LCASE(""{safeName}"")))
  ?item wdt:P345 ?imdbId.
}}
LIMIT 1";

            url = $"{WikidataSparqlEndpoint}?format=json&query={Uri.EscapeDataString(sparqlQueryFuzzy)}";

            using var response2 = await httpClient.GetAsync(url, cancellationToken);
            if (response2.IsSuccessStatusCode)
            {
                var json2 = await response2.Content.ReadAsStringAsync(cancellationToken);
                var sparqlResult2 = JsonSerializer.Deserialize<SparqlResult>(json2, JsonOptions.Default);
                var binding2 = sparqlResult2?.Results?.Bindings?.Count > 0 ? sparqlResult2.Results.Bindings[0] : null;

                if (binding2?.ImdbId?.Value != null)
                {
                    _logger.LogInformation("RussianMetadata (Series): Found IMDb ID {ImdbId} by fuzzy name '{CleanName}'", binding2.ImdbId.Value, cleanName);
                    return binding2.ImdbId.Value;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RussianMetadata (Series): Error searching IMDb ID by name");
        }

        return null;
    }

    // ───── Wikidata ─────

    private async Task<WikidataEntity?> FetchWikidataEntityByImdbId(string imdbId, CancellationToken cancellationToken)
    {
        // Use raw HttpClient to avoid any IHttpClientFactory issues
        using var httpClient = _httpClientFactory.CreateClient("RussianMetadata");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Jellyfin-RussianMetadata/1.0");
        httpClient.Timeout = TimeSpan.FromSeconds(30);

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

        _logger.LogInformation("RussianMetadata (Series): Wikidata query for {ImdbId}", imdbId);

        try
        {
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("RussianMetadata (Series): Wikidata HTTP {Status} for {ImdbId}", (int)response.StatusCode, imdbId);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var sparqlResult = JsonSerializer.Deserialize<SparqlResult>(json, JsonOptions.Default);
            var binding = sparqlResult?.Results?.Bindings?.Count > 0 ? sparqlResult.Results.Bindings[0] : null;

            if (binding == null)
            {
                _logger.LogWarning("RussianMetadata (Series): No Wikidata bindings for {ImdbId}", imdbId);
                return null;
            }

            _logger.LogInformation("RussianMetadata (Series): Wikidata found EN label '{label}', RU label '{ru}' for {ImdbId}",
                binding.EnLabel?.Value ?? "?", binding.RuLabel?.Value ?? "?", imdbId);

            return new WikidataEntity
            {
                EntityId = binding.Item?.Value,
                RussianLabel = binding.RuLabel?.Value,
                EnglishLabel = binding.EnLabel?.Value,
                RussianDescription = binding.RuDescription?.Value,
                EnglishDescription = binding.EnDescription?.Value
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RussianMetadata (Series): Wikidata fetch error for {ImdbId}", imdbId);
            return null;
        }
    }

    // ───── Wikidata by name (no IMDb ID needed) ─────

    private async Task<bool> TryWikidataByName(string seriesName, Configuration.PluginConfiguration config,
        MetadataResult<Series> result, CancellationToken cancellationToken)
    {
        _logger.LogInformation("RussianMetadata (Series): Wikidata lookup by name: {Name}", seriesName);

        try
        {
            var cleanName = CleanSeriesName(seriesName);
            if (string.IsNullOrWhiteSpace(cleanName)) return false;

            using var httpClient = _httpClientFactory.CreateClient("RussianMetadata");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Jellyfin-RussianMetadata/1.0");
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            // Try exact match on any language label, then grab RU label/description
            var safeName = EscapeSparqlString(cleanName);
            var sparqlQuery = $@"
SELECT ?item ?ruLabel ?ruDescription WHERE {{
  ?item wdt:P31/wdt:P279* wd:Q15416.
  ?item rdfs:label ?label.
  FILTER(LCASE(?label) = LCASE(""{safeName}""))
  OPTIONAL {{ ?item rdfs:label ?ruLabel. FILTER(LANG(?ruLabel) = ""ru"") }}
  OPTIONAL {{ ?item schema:description ?ruDescription. FILTER(LANG(?ruDescription) = ""ru"") }}
}}
LIMIT 1";

            var url = $"{WikidataSparqlEndpoint}?format=json&query={Uri.EscapeDataString(sparqlQuery)}";

            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var sparqlResult = JsonSerializer.Deserialize<SparqlResult>(json, JsonOptions.Default);
                var binding = sparqlResult?.Results?.Bindings?.Count > 0 ? sparqlResult.Results.Bindings[0] : null;

                if (binding?.RuLabel?.Value != null)
                {
                    result.Item ??= new Series();

                    if (config.EnableRussianTitles)
                        result.Item.Name = binding.RuLabel.Value;

                    if (config.EnableRussianOverviews && binding.RuDescription?.Value != null)
                        result.Item.Overview = binding.RuDescription.Value;

                    _logger.LogInformation("RussianMetadata (Series): Wikidata by name — '{Name}' -> '{RuName}'", seriesName, binding.RuLabel.Value);
                    return true;
                }
            }

            // Fuzzy fallback: CONTAINS on any language label
            var sparqlFuzzy = $@"
SELECT ?item ?ruLabel ?ruDescription WHERE {{
  ?item wdt:P31/wdt:P279* wd:Q15416.
  ?item rdfs:label ?label.
  FILTER(CONTAINS(LCASE(?label), LCASE(""{safeName}"")))
  OPTIONAL {{ ?item rdfs:label ?ruLabel. FILTER(LANG(?ruLabel) = ""ru"") }}
  OPTIONAL {{ ?item schema:description ?ruDescription. FILTER(LANG(?ruDescription) = ""ru"") }}
}}
LIMIT 1";

            url = $"{WikidataSparqlEndpoint}?format=json&query={Uri.EscapeDataString(sparqlFuzzy)}";
            using var response2 = await httpClient.GetAsync(url, cancellationToken);
            if (response2.IsSuccessStatusCode)
            {
                var json2 = await response2.Content.ReadAsStringAsync(cancellationToken);
                var sparqlResult2 = JsonSerializer.Deserialize<SparqlResult>(json2, JsonOptions.Default);
                var binding2 = sparqlResult2?.Results?.Bindings?.Count > 0 ? sparqlResult2.Results.Bindings[0] : null;

                if (binding2?.RuLabel?.Value != null)
                {
                    result.Item ??= new Series();

                    if (config.EnableRussianTitles)
                        result.Item.Name = binding2.RuLabel.Value;

                    if (config.EnableRussianOverviews && binding2.RuDescription?.Value != null)
                        result.Item.Overview = binding2.RuDescription.Value;

                    _logger.LogInformation("RussianMetadata (Series): Wikidata by name (fuzzy) — '{Name}' -> '{RuName}'", seriesName, binding2.RuLabel.Value);
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "RussianMetadata (Series): Wikidata by name error for {Name}", seriesName);
        }

        return false;
    }

    // ───── Search ─────

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo searchInfo, CancellationToken cancellationToken)
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
                var searchUrl = $"{TmdbApiBase}/search/tv?api_key={Uri.EscapeDataString(config.TmdbApiKey)}&language=ru-RU&query={query}";

                using var response = await httpClient.GetAsync(searchUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    var searchResult = JsonSerializer.Deserialize<SeriesTmdbTvSearchResponse>(json, JsonOptions.Default);
                    if (searchResult?.Results != null)
                    {
                        foreach (var s in searchResult.Results)
                        {
                            var sr = new RemoteSearchResult
                            {
                                Name = s.Name ?? s.OriginalName ?? "Unknown",
                                Overview = s.Overview,
                                SearchProviderName = Name,
                                ProductionYear = s.FirstAirDate?.Length >= 4
                                    && int.TryParse(s.FirstAirDate[..4], out var yr) ? yr : null
                            };
                            sr.SetProviderId("Tmdb", s.Id.ToString(CultureInfo.InvariantCulture));
                            results.Add(sr);
                        }
                        return results;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "RussianMetadata (Series): TMDB search failed, fallback to Wikidata");
            }
        }

        // Fallback: Wikidata search for TV series (Q15416 = television series)
        try
        {
            using var httpClient = _httpClientFactory.CreateClient("RussianMetadata");
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Jellyfin-RussianMetadata/1.0");
            httpClient.Timeout = TimeSpan.FromSeconds(5);

            var safeName = EscapeSparqlString(searchInfo.Name);
            var sparqlQuery = $@"
SELECT ?item ?itemLabel ?description WHERE {{
  ?item wdt:P31/wdt:P279* wd:Q15416.
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
            _logger.LogError(ex, "RussianMetadata (Series): Wikidata search error");
        }

        return results;
    }

    public Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("RussianMetadata");
        return httpClient.GetAsync(url, cancellationToken);
    }
}

// ───── TMDB JSON models for TV ─────

internal class SeriesTmdbTvRef
{
    public int Id { get; set; }
}

internal class SeriesTmdbTvDetails
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? Overview { get; set; }
    [JsonPropertyName("original_name")]
    public string? OriginalName { get; set; }
    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }
}

internal class SeriesTmdbTvSearchResponse
{
    public List<SeriesTmdbTvSearchItem>? Results { get; set; }
}

internal class SeriesTmdbTvSearchItem
{
    public int Id { get; set; }
    public string? Name { get; set; }
    [JsonPropertyName("original_name")]
    public string? OriginalName { get; set; }
    public string? Overview { get; set; }
    [JsonPropertyName("first_air_date")]
    public string? FirstAirDate { get; set; }
}
