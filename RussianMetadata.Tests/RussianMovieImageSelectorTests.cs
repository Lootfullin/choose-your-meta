using System;
using System.Linq;
using MediaBrowser.Model.Entities;
using Xunit;

namespace RussianMetadata.Tests;

public sealed class RussianMovieImageSelectorTests
{
    [Fact]
    public void Select_ReturnsOnlyRussianPostersAndLogos()
    {
        var images = new TmdbMovieImages
        {
            Posters =
            [
                Image("/ru-poster.jpg", "ru", 7.5, 10),
                Image("/en-poster.jpg", "en", 9.0, 100),
                Image("/untagged-poster.jpg", null, 10.0, 200)
            ],
            Logos =
            [
                Image("/ru-logo.png", "ru", 6.0, 5),
                Image("/en-logo.png", "en", 8.0, 50)
            ]
        };

        var result = RussianMovieImageSelector.Select(
            images,
            includePosters: true,
            includeLogos: true,
            providerName: "test");

        Assert.Collection(
            result,
            poster =>
            {
                Assert.Equal(ImageType.Primary, poster.Type);
                Assert.EndsWith("/ru-poster.jpg", poster.Url);
                Assert.Equal("ru", poster.Language);
            },
            logo =>
            {
                Assert.Equal(ImageType.Logo, logo.Type);
                Assert.EndsWith("/ru-logo.png", logo.Url);
                Assert.Equal("ru", logo.Language);
            });
    }

    [Fact]
    public void Select_ReturnsNothingWhenRussianArtworkDoesNotExist()
    {
        var images = new TmdbMovieImages
        {
            Posters = [Image("/en.jpg", "en", 10.0, 100)],
            Logos = [Image("/untagged.png", null, 10.0, 100)]
        };

        var result = RussianMovieImageSelector.Select(
            images,
            includePosters: true,
            includeLogos: true,
            providerName: "test");

        Assert.Empty(result);
    }

    [Fact]
    public void Select_OrdersRussianArtworkByRatingThenVotes()
    {
        var images = new TmdbMovieImages
        {
            Posters =
            [
                Image("/second.jpg", "ru", 7.0, 2),
                Image("/third.jpg", "ru", 6.0, 500),
                Image("/first.jpg", "ru", 7.0, 20)
            ]
        };

        var result = RussianMovieImageSelector.Select(
            images,
            includePosters: true,
            includeLogos: false,
            providerName: "test");

        Assert.Equal(
            ["first.jpg", "second.jpg", "third.jpg"],
            result.Select(image => new Uri(image.Url).Segments.Last()).ToArray());
    }

    private static TmdbImageFile Image(
        string path,
        string? language,
        double rating,
        int votes)
    {
        return new TmdbImageFile
        {
            FilePath = path,
            Language = language,
            VoteAverage = rating,
            VoteCount = votes,
            Width = 1000,
            Height = 1500
        };
    }
}
