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
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using RussianMetadata.Configuration;

namespace RussianMetadata;

/// <summary>
/// Supplies only TMDb movie artwork explicitly tagged as Russian.
/// Other enabled Jellyfin image providers remain the fallback when TMDb has no
/// matching Russian artwork.
/// </summary>
public sealed class RussianMovieImageProvider : IRemoteImageProvider, IDisposable
{
    private const string TmdbApiBase = "https://api.themoviedb.org/3";
    private readonly object _httpClientLock = new();
    private readonly ILogger<RussianMovieImageProvider> _logger;
    private readonly Dictionary<string, HttpClient> _httpClients = [];

    public RussianMovieImageProvider(ILogger<RussianMovieImageProvider> logger)
    {
        _logger = logger;
    }

    public string Name => "Russian Metadata — русские изображения";

    private PluginConfiguration Configuration =>
        Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public bool Supports(BaseItem item) => item is Movie;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        if (Configuration.EnableRussianPosters)
        {
            yield return ImageType.Primary;
        }

        if (Configuration.EnableRussianLogos)
        {
            yield return ImageType.Logo;
        }
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(
        BaseItem item,
        CancellationToken cancellationToken)
    {
        var config = Configuration;
        if (string.IsNullOrWhiteSpace(config.TmdbApiKey)
            || (!config.EnableRussianPosters && !config.EnableRussianLogos))
        {
            return [];
        }

        try
        {
            var httpClient = GetHttpClient(config);

            var tmdbId = await ResolveTmdbId(
                item,
                config.TmdbApiKey,
                httpClient,
                cancellationToken);
            if (tmdbId <= 0)
            {
                return [];
            }

            // The response is filtered again locally. This protects against
            // untagged artwork being returned by an upstream API behavior change.
            var imagesUrl =
                $"{TmdbApiBase}/movie/{tmdbId.ToString(CultureInfo.InvariantCulture)}/images"
                + $"?api_key={Uri.EscapeDataString(config.TmdbApiKey)}"
                + "&include_image_language=ru";
            using var response = await httpClient.GetAsync(
                imagesUrl,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "RussianMetadata images: TMDB request failed for movie {TmdbId} ({Status})",
                    tmdbId,
                    response.StatusCode);
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var images = JsonSerializer.Deserialize<TmdbMovieImages>(
                json,
                JsonOptions.Default);
            var result = RussianMovieImageSelector.Select(
                images,
                config.EnableRussianPosters,
                config.EnableRussianLogos,
                Name);

            _logger.LogInformation(
                "RussianMetadata images: found {Count} Russian poster/logo candidates for TMDB movie {TmdbId}",
                result.Count,
                tmdbId);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "RussianMetadata images: failed; the next Jellyfin image provider remains available");
            return [];
        }
    }

    public async Task<HttpResponseMessage> GetImageResponse(
        string url,
        CancellationToken cancellationToken)
    {
        return await GetHttpClient(Configuration).GetAsync(
            url,
            cancellationToken);
    }

    public void Dispose()
    {
        lock (_httpClientLock)
        {
            foreach (var httpClient in _httpClients.Values)
            {
                httpClient.Dispose();
            }

            _httpClients.Clear();
        }
    }

    private static async Task<int> ResolveTmdbId(
        BaseItem item,
        string apiKey,
        HttpClient httpClient,
        CancellationToken cancellationToken)
    {
        if (int.TryParse(
            item.GetProviderId(MetadataProvider.Tmdb),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var tmdbId)
            && tmdbId > 0)
        {
            return tmdbId;
        }

        var imdbId = item.GetProviderId(MetadataProvider.Imdb);
        if (string.IsNullOrWhiteSpace(imdbId))
        {
            return 0;
        }

        var findUrl =
            $"{TmdbApiBase}/find/{Uri.EscapeDataString(imdbId)}"
            + $"?api_key={Uri.EscapeDataString(apiKey)}"
            + "&external_source=imdb_id";
        using var response = await httpClient.GetAsync(findUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return 0;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var find = JsonSerializer.Deserialize<TmdbFindResult>(
            json,
            JsonOptions.Default);
        return find?.MovieResults?.FirstOrDefault()?.Id ?? 0;
    }

    private static HttpClientHandler CreateProxyHandler(
        PluginConfiguration config)
    {
        var handler = new HttpClientHandler();
        if (string.IsNullOrWhiteSpace(config.ProxyUrl)
            || !Uri.TryCreate(config.ProxyUrl, UriKind.Absolute, out var proxyUri)
            || (proxyUri.Scheme != Uri.UriSchemeHttp
                && proxyUri.Scheme != Uri.UriSchemeHttps))
        {
            return handler;
        }

        var proxy = new WebProxy(proxyUri);
        if (!string.IsNullOrWhiteSpace(config.ProxyUsername))
        {
            proxy.Credentials = new NetworkCredential(
                config.ProxyUsername,
                config.ProxyPassword);
        }

        handler.Proxy = proxy;
        handler.UseProxy = true;
        return handler;
    }

    private HttpClient GetHttpClient(PluginConfiguration config)
    {
        var configurationKey = string.Join(
            "\n",
            config.ProxyUrl,
            config.ProxyUsername,
            config.ProxyPassword);

        lock (_httpClientLock)
        {
            if (_httpClients.TryGetValue(
                configurationKey,
                out var existingClient))
            {
                return existingClient;
            }

            var httpClient = new HttpClient(
                CreateProxyHandler(config),
                disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept",
                "application/json");
            _httpClients.Add(configurationKey, httpClient);
            return httpClient;
        }
    }
}

internal static class RussianMovieImageSelector
{
    private const string OriginalImageBase = "https://image.tmdb.org/t/p/original";
    private const string PosterThumbnailBase = "https://image.tmdb.org/t/p/w342";
    private const string LogoThumbnailBase = "https://image.tmdb.org/t/p/w500";

    internal static List<RemoteImageInfo> Select(
        TmdbMovieImages? images,
        bool includePosters,
        bool includeLogos,
        string providerName)
    {
        var result = new List<RemoteImageInfo>();
        if (includePosters)
        {
            result.AddRange(Convert(
                images?.Posters,
                ImageType.Primary,
                PosterThumbnailBase,
                providerName));
        }

        if (includeLogos)
        {
            result.AddRange(Convert(
                images?.Logos,
                ImageType.Logo,
                LogoThumbnailBase,
                providerName));
        }

        return result;
    }

    private static IEnumerable<RemoteImageInfo> Convert(
        IEnumerable<TmdbImageFile>? images,
        ImageType type,
        string thumbnailBase,
        string providerName)
    {
        return images?
            .Where(image =>
                string.Equals(
                    image.Language,
                    "ru",
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(image.FilePath))
            .OrderByDescending(image => image.VoteAverage ?? 0)
            .ThenByDescending(image => image.VoteCount ?? 0)
            .Select(image => new RemoteImageInfo
            {
                ProviderName = providerName,
                Url = OriginalImageBase + image.FilePath,
                ThumbnailUrl = thumbnailBase + image.FilePath,
                Height = image.Height,
                Width = image.Width,
                CommunityRating = image.VoteAverage,
                VoteCount = image.VoteCount,
                Language = "ru",
                Type = type,
                RatingType = RatingType.Score
            })
            ?? [];
    }
}

internal sealed class TmdbMovieImages
{
    public List<TmdbImageFile>? Posters { get; set; }

    public List<TmdbImageFile>? Logos { get; set; }
}

internal sealed class TmdbImageFile
{
    [JsonPropertyName("file_path")]
    public string? FilePath { get; set; }

    [JsonPropertyName("iso_639_1")]
    public string? Language { get; set; }

    public int? Height { get; set; }

    public int? Width { get; set; }

    [JsonPropertyName("vote_average")]
    public double? VoteAverage { get; set; }

    [JsonPropertyName("vote_count")]
    public int? VoteCount { get; set; }
}
