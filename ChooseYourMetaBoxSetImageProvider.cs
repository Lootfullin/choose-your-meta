using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using RussianMetadata.Configuration;

namespace RussianMetadata;

public sealed class ChooseYourMetaBoxSetImageProvider
    : IRemoteImageProvider,
        IDisposable
{
    private const string TmdbApiBase = "https://api.themoviedb.org/3";
    private readonly Dictionary<string, HttpClient> _httpClients = [];
    private readonly object _httpClientLock = new();
    private readonly ILogger<ChooseYourMetaBoxSetImageProvider> _logger;

    public ChooseYourMetaBoxSetImageProvider(
        ILogger<ChooseYourMetaBoxSetImageProvider> logger)
    {
        _logger = logger;
    }

    public string Name => "Choose your Meta! — изображения";

    private PluginConfiguration Configuration =>
        Plugin.Instance?.Configuration ?? new PluginConfiguration();

    public bool Supports(BaseItem item) => item is BoxSet;

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        if (Configuration.CollectionPosterPreference
            != ArtworkLanguagePreference.Disabled)
        {
            yield return ImageType.Primary;
        }

    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(
        BaseItem item,
        CancellationToken cancellationToken)
    {
        var config = Configuration;
        var apiKey = TmdbApiKeyResolver.Resolve(config);
        var tmdbId = ParseTmdbId(item.GetProviderId(MetadataProvider.Tmdb));
        if (string.IsNullOrWhiteSpace(apiKey) || tmdbId <= 0)
        {
            return [];
        }

        try
        {
            var url = $"{TmdbApiBase}/collection/{tmdbId.ToString(CultureInfo.InvariantCulture)}"
                + $"?api_key={Uri.EscapeDataString(apiKey)}"
                + "&language=ru-RU"
                + "&append_to_response=images"
                + "&include_image_language=ru,en";
            using var response = await GetHttpClient(config).GetAsync(
                url,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(
                cancellationToken);
            var collection =
                JsonSerializer.Deserialize<TmdbCollectionArtworkResponse>(
                    json,
                    JsonOptions.Default);
            return ArtworkSelector.Select(
                collection?.Images,
                config.CollectionPosterPreference,
                ArtworkLanguagePreference.Disabled,
                Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "ChooseYourMeta: TMDB collection images failed");
            return [];
        }
    }

    public Task<HttpResponseMessage> GetImageResponse(
        string url,
        CancellationToken cancellationToken)
    {
        return GetHttpClient(Configuration).GetAsync(url, cancellationToken);
    }

    public void Dispose()
    {
        lock (_httpClientLock)
        {
            foreach (var client in _httpClients.Values)
            {
                client.Dispose();
            }

            _httpClients.Clear();
        }
    }

    private HttpClient GetHttpClient(PluginConfiguration config)
    {
        var key = string.Join(
            "\n",
            config.ProxyUrl,
            config.ProxyUsername,
            config.ProxyPassword);
        lock (_httpClientLock)
        {
            if (_httpClients.TryGetValue(key, out var existingClient))
            {
                return existingClient;
            }

            var handler = new HttpClientHandler();
            if (!string.IsNullOrWhiteSpace(config.ProxyUrl)
                && Uri.TryCreate(
                    config.ProxyUrl,
                    UriKind.Absolute,
                    out var proxyUri)
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

            var client = new HttpClient(handler, disposeHandler: true)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            _httpClients.Add(key, client);
            return client;
        }
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
}

internal sealed class TmdbCollectionArtworkResponse
{
    public TmdbArtworkImages? Images { get; set; }
}
