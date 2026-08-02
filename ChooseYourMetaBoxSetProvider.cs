using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using RussianMetadata.Configuration;

namespace RussianMetadata;

public sealed class ChooseYourMetaBoxSetProvider
    : IRemoteMetadataProvider<BoxSet, BoxSetInfo>,
      ICustomMetadataProvider<BoxSet>
{
    private const string TmdbApiBase = "https://api.themoviedb.org/3";
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<ChooseYourMetaBoxSetProvider> _logger;

    public ChooseYourMetaBoxSetProvider(
        IHttpClientFactory httpClientFactory,
        ILibraryManager libraryManager,
        ILogger<ChooseYourMetaBoxSetProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public string Name => "Choose your Meta!";

    private PluginConfiguration Configuration =>
        Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public async Task<MetadataResult<BoxSet>> GetMetadata(
        BoxSetInfo info,
        CancellationToken cancellationToken)
    {
        var result = new MetadataResult<BoxSet>();
        var apiKey = TmdbApiKeyResolver.Resolve(Configuration);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return result;
        }

        try
        {
            using var httpClient = CreateHttpClient(Configuration);
            var tmdbId = ParseTmdbId(info.GetProviderId(MetadataProvider.Tmdb));
            if (tmdbId <= 0 && !string.IsNullOrWhiteSpace(info.Name))
            {
                tmdbId = await SearchCollectionId(
                    _libraryManager.ParseName(info.Name).Name,
                    apiKey,
                    httpClient,
                    cancellationToken);
            }

            var collection = await GetCollection(
                tmdbId,
                apiKey,
                httpClient,
                cancellationToken);
            if (collection is null)
            {
                return result;
            }

            var item = new BoxSet();
            ApplyRussianLanguagePreference(item);
            var russianName = MovieTextLocalization.RussianOrNull(
                collection.Name);
            var russianOverview = MovieTextLocalization.RussianOrNull(
                collection.Overview);
            if (Configuration.EnableRussianTitles
                && !string.IsNullOrWhiteSpace(russianName))
            {
                item.Name = russianName;
            }
            else if (!string.IsNullOrWhiteSpace(info.Name))
            {
                item.Name = info.Name;
            }

            if (Configuration.EnableRussianOverviews
                && !string.IsNullOrWhiteSpace(russianOverview))
            {
                item.Overview = russianOverview;
            }

            item.SetProviderId(
                MetadataProvider.Tmdb,
                collection.Id.ToString(CultureInfo.InvariantCulture));
            result.Item = item;
            result.HasMetadata = true;
            result.ResultLanguage = "ru";
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "ChooseYourMeta: TMDB collection metadata failed");
            return result;
        }
    }

    public async Task<ItemUpdateType> FetchAsync(
        BoxSet item,
        MetadataRefreshOptions options,
        CancellationToken cancellationToken)
    {
        var config = Configuration;
        if (!config.EnableRussianTitles && !config.EnableRussianOverviews)
        {
            return ItemUpdateType.None;
        }

        var tmdbId = ParseTmdbId(item.GetProviderId(MetadataProvider.Tmdb));
        var apiKey = TmdbApiKeyResolver.Resolve(config);
        if (tmdbId <= 0 || string.IsNullOrWhiteSpace(apiKey))
        {
            return ItemUpdateType.None;
        }

        try
        {
            ApplyRussianLanguagePreference(item);
            using var httpClient = CreateHttpClient(config);
            var collection = await GetCollection(tmdbId, apiKey, httpClient, cancellationToken);
            if (collection is null)
            {
                return ItemUpdateType.None;
            }

            var changed = false;
            var russianName = MovieTextLocalization.RussianOrNull(collection.Name);
            if (config.EnableRussianTitles && !string.IsNullOrWhiteSpace(russianName) && item.Name != russianName)
            {
                item.Name = russianName;
                changed = true;
            }

            var russianOverview = MovieTextLocalization.RussianOrNull(collection.Overview);
            if (config.EnableRussianOverviews
                && !string.IsNullOrWhiteSpace(russianOverview)
                && item.Overview != russianOverview)
            {
                item.Overview = russianOverview;
                changed = true;
            }

            return changed ? ItemUpdateType.MetadataEdit : ItemUpdateType.None;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ChooseYourMeta: final BoxSet localization failed for TMDB {TmdbId}", tmdbId);
            return ItemUpdateType.None;
        }
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(
        BoxSetInfo searchInfo,
        CancellationToken cancellationToken)
    {
        var apiKey = TmdbApiKeyResolver.Resolve(Configuration);
        if (string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(searchInfo.Name))
        {
            return [];
        }

        try
        {
            using var httpClient = CreateHttpClient(Configuration);
            var url = $"{TmdbApiBase}/search/collection"
                + $"?api_key={Uri.EscapeDataString(apiKey)}"
                + "&language=ru-RU"
                + $"&query={Uri.EscapeDataString(searchInfo.Name)}";
            using var response = await httpClient.GetAsync(
                url,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(
                cancellationToken);
            var search = JsonSerializer.Deserialize<TmdbCollectionSearchResponse>(
                json,
                JsonOptions.Default);
            return search?.Results?
                .Select(collection =>
                {
                    var result = new RemoteSearchResult
                    {
                        Name = collection.Name ?? searchInfo.Name,
                        Overview = collection.Overview,
                        SearchProviderName = Name,
                        ImageUrl = string.IsNullOrWhiteSpace(
                            collection.PosterPath)
                            ? null
                            : "https://image.tmdb.org/t/p/original"
                                + collection.PosterPath
                    };
                    result.SetProviderId(
                        MetadataProvider.Tmdb,
                        collection.Id.ToString(CultureInfo.InvariantCulture));
                    return result;
                })
                .ToArray()
                ?? [];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "ChooseYourMeta: TMDB collection search failed");
            return [];
        }
    }

    public Task<HttpResponseMessage> GetImageResponse(
        string url,
        CancellationToken cancellationToken)
    {
        return _httpClientFactory
            .CreateClient("ChooseYourMeta")
            .GetAsync(url, cancellationToken);
    }

    private static async Task<int> SearchCollectionId(
        string name,
        string apiKey,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        var url = $"{TmdbApiBase}/search/collection"
            + $"?api_key={Uri.EscapeDataString(apiKey)}"
            + "&language=ru-RU"
            + $"&query={Uri.EscapeDataString(name)}";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return 0;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var search = JsonSerializer.Deserialize<TmdbCollectionSearchResponse>(
            json,
            JsonOptions.Default);
        return search?.Results?.FirstOrDefault()?.Id ?? 0;
    }

    private static async Task<TmdbCollectionDetails?> GetCollection(
        int tmdbId,
        string apiKey,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        if (tmdbId <= 0)
        {
            return null;
        }

        var url = $"{TmdbApiBase}/collection/{tmdbId.ToString(CultureInfo.InvariantCulture)}"
            + $"?api_key={Uri.EscapeDataString(apiKey)}"
            + "&language=ru-RU";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        return JsonSerializer.Deserialize<TmdbCollectionDetails>(
            json,
            JsonOptions.Default);
    }

    private static int ParseTmdbId(string? value)
    {
        return int.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var id)
            ? id
            : 0;
    }

    private static void ApplyRussianLanguagePreference(BoxSet item)
    {
        item.PreferredMetadataLanguage = "ru";
        item.PreferredMetadataCountryCode = "RU";
    }

    private static HttpClient CreateHttpClient(PluginConfiguration config)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrWhiteSpace(config.ProxyUrl)
            && Uri.TryCreate(config.ProxyUrl, UriKind.Absolute, out var proxyUri)
            && (proxyUri.Scheme == Uri.UriSchemeHttp
                || proxyUri.Scheme == Uri.UriSchemeHttps))
        {
            var proxy = new WebProxy(proxyUri);
            if (!string.IsNullOrWhiteSpace(config.ProxyUsername))
            {
                proxy.Credentials = new NetworkCredential(
                    config.ProxyUsername,
                    config.ProxyPassword);
            }

            handler.Proxy = proxy;
            handler.UseProxy = true;
        }

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }
}

internal sealed class TmdbCollectionSearchResponse
{
    public List<TmdbCollectionDetails>? Results { get; set; }
}

internal sealed class TmdbCollectionDetails
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public string? Overview { get; set; }

    [JsonPropertyName("poster_path")]
    public string? PosterPath { get; set; }
}
