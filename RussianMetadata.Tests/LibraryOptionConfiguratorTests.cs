using System.Linq;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Entities;
using Xunit;

namespace RussianMetadata.Tests;

public sealed class LibraryOptionConfiguratorTests
{
    [Fact]
    public void Apply_MovieLibrary_PrependsProvidersForMoviesAndCollections()
    {
        var options = new LibraryOptions
        {
            TypeOptions =
            [
                new TypeOptions
                {
                    Type = "Movie",
                    MetadataFetchers = ["TheMovieDb", "Russian Metadata"],
                    MetadataFetcherOrder = ["TheMovieDb", "Russian Metadata"],
                    ImageFetchers =
                    [
                        "TheMovieDb",
                        "Russian Metadata — русские изображения"
                    ],
                    ImageFetcherOrder =
                    [
                        "TheMovieDb",
                        "Russian Metadata — русские изображения"
                    ]
                }
            ]
        };

        var changed = LibraryOptionConfigurator.Apply(
            options,
            CollectionTypeOptions.movies);

        Assert.True(changed);
        var movie = options.GetTypeOptions("Movie")!;
        Assert.Equal(
            LibraryOptionConfigurator.MetadataProviderName,
            movie.MetadataFetcherOrder[0]);
        Assert.Equal(
            LibraryOptionConfigurator.ImageProviderName,
            movie.ImageFetcherOrder[0]);
        Assert.DoesNotContain("Russian Metadata", movie.MetadataFetchers);
        Assert.NotNull(options.GetTypeOptions("BoxSet"));
        Assert.Equal(
            LibraryOptionConfigurator.CustomArtworkProviderName,
            options.GetTypeOptions("BoxSet")!.ImageFetcherOrder[0]);
    }

    [Fact]
    public void Apply_IsIdempotentAndPreservesOtherProviders()
    {
        var options = new LibraryOptions
        {
            TypeOptions =
            [
                new TypeOptions
                {
                    Type = "BoxSet",
                    MetadataFetchers = ["TheMovieDb"],
                    MetadataFetcherOrder = ["TheMovieDb"],
                    ImageFetchers = ["TheMovieDb"],
                    ImageFetcherOrder = ["TheMovieDb"]
                }
            ]
        };

        Assert.True(LibraryOptionConfigurator.Apply(
            options,
            CollectionTypeOptions.boxsets));
        Assert.False(LibraryOptionConfigurator.Apply(
            options,
            CollectionTypeOptions.boxsets));
        Assert.Contains(
            "TheMovieDb",
            options.GetTypeOptions("BoxSet")!.MetadataFetchers);
    }

    [Fact]
    public void Apply_TvLibrary_DoesNotAddImageProvider()
    {
        var options = new LibraryOptions();

        Assert.True(LibraryOptionConfigurator.Apply(
            options,
            CollectionTypeOptions.tvshows));

        Assert.All(
            options.TypeOptions,
            itemOptions => Assert.Empty(itemOptions.ImageFetchers));
        Assert.Equal(
            ["Episode", "Season", "Series"],
            options.TypeOptions.Select(value => value.Type).Order().ToArray());
    }
}
