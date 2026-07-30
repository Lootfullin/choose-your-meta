using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace RussianMetadata;

public sealed class LibraryConfigurationService
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<LibraryConfigurationService> _logger;

    public LibraryConfigurationService(
        ILibraryManager libraryManager,
        ILogger<LibraryConfigurationService> logger)
    {
        _libraryManager = libraryManager;
        _logger = logger;
    }

    public LibraryConfigurationResult Apply()
    {
        var updated = new List<string>();
        var skipped = new List<string>();
        foreach (var folder in _libraryManager.GetVirtualFolders())
        {
            if (!IsSupported(folder.CollectionType)
                || !Guid.TryParse(folder.ItemId, out var itemId))
            {
                skipped.Add(folder.Name);
                continue;
            }

            var collectionFolder =
                _libraryManager.GetItemById<CollectionFolder>(itemId);
            if (collectionFolder is null)
            {
                skipped.Add(folder.Name);
                continue;
            }

            var options = collectionFolder.GetLibraryOptions();
            if (LibraryOptionConfigurator.Apply(
                options,
                folder.CollectionType!.Value))
            {
                collectionFolder.UpdateLibraryOptions(options);
                updated.Add(folder.Name);
                _logger.LogInformation(
                    "ChooseYourMeta: configured library {LibraryName}",
                    folder.Name);
            }
        }

        return new LibraryConfigurationResult(updated, skipped);
    }

    private static bool IsSupported(CollectionTypeOptions? type)
    {
        return type is CollectionTypeOptions.movies
            or CollectionTypeOptions.tvshows
            or CollectionTypeOptions.mixed
            or CollectionTypeOptions.boxsets;
    }
}

internal static class LibraryOptionConfigurator
{
    internal const string MetadataProviderName = "Choose your Meta!";
    internal const string ImageProviderName =
        "Choose your Meta! — изображения";

    private static readonly string[] LegacyMetadataNames =
    [
        "Russian Metadata"
    ];

    private static readonly string[] LegacyImageNames =
    [
        "Russian Metadata — русские изображения"
    ];

    internal static bool Apply(
        LibraryOptions options,
        CollectionTypeOptions collectionType)
    {
        var types = collectionType switch
        {
            CollectionTypeOptions.movies => new[] { "Movie", "BoxSet" },
            CollectionTypeOptions.tvshows => new[] { "Series", "Episode" },
            CollectionTypeOptions.mixed =>
                new[] { "Movie", "BoxSet", "Series", "Episode" },
            CollectionTypeOptions.boxsets => new[] { "BoxSet" },
            _ => []
        };
        if (types.Length == 0)
        {
            return false;
        }

        var changed = false;
        var typeOptions = (options.TypeOptions ?? []).ToList();
        foreach (var type in types)
        {
            var itemOptions = typeOptions.FirstOrDefault(existing =>
                string.Equals(
                    existing.Type,
                    type,
                    StringComparison.OrdinalIgnoreCase));
            if (itemOptions is null)
            {
                itemOptions = new TypeOptions
                {
                    Type = type,
                    MetadataFetchers = ["TheMovieDb"],
                    MetadataFetcherOrder = ["TheMovieDb"],
                    ImageFetchers = type is "Movie" or "BoxSet"
                        ? ["TheMovieDb"]
                        : [],
                    ImageFetcherOrder = type is "Movie" or "BoxSet"
                        ? ["TheMovieDb"]
                        : []
                };
                typeOptions.Add(itemOptions);
                changed = true;
            }

            var metadataFetchers = Prepend(
                itemOptions.MetadataFetchers,
                MetadataProviderName,
                LegacyMetadataNames);
            itemOptions.MetadataFetchers = metadataFetchers.Values;
            changed |= metadataFetchers.Changed;
            var metadataOrder = Prepend(
                itemOptions.MetadataFetcherOrder,
                MetadataProviderName,
                LegacyMetadataNames);
            itemOptions.MetadataFetcherOrder = metadataOrder.Values;
            changed |= metadataOrder.Changed;

            if (type is "Movie" or "BoxSet")
            {
                var imageFetchers = Prepend(
                    itemOptions.ImageFetchers,
                    ImageProviderName,
                    LegacyImageNames);
                itemOptions.ImageFetchers = imageFetchers.Values;
                changed |= imageFetchers.Changed;
                var imageOrder = Prepend(
                    itemOptions.ImageFetcherOrder,
                    ImageProviderName,
                    LegacyImageNames);
                itemOptions.ImageFetcherOrder = imageOrder.Values;
                changed |= imageOrder.Changed;
            }
        }

        if (changed)
        {
            options.TypeOptions = typeOptions.ToArray();
        }

        return changed;
    }

    private static (string[] Values, bool Changed) Prepend(
        string[]? values,
        string providerName,
        IReadOnlyCollection<string> legacyNames)
    {
        values ??= [];
        var updated = new[] { providerName }
            .Concat(values.Where(value =>
                !string.Equals(
                    value,
                    providerName,
                    StringComparison.OrdinalIgnoreCase)
                && !legacyNames.Contains(
                    value,
                    StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        if (values.SequenceEqual(updated, StringComparer.Ordinal))
        {
            return (values, false);
        }

        return (updated, true);
    }
}

public sealed record LibraryConfigurationResult(
    IReadOnlyList<string> UpdatedLibraries,
    IReadOnlyList<string> SkippedLibraries);
