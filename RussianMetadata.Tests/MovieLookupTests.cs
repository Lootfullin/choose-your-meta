using System.Collections.Generic;
using Xunit;

namespace RussianMetadata.Tests;

public sealed class MovieLookupTests
{
    [Theory]
    [InlineData("(1967) You Only Live Twice", null, "You Only Live Twice", 1967)]
    [InlineData("You Only Live Twice (1967)", null, "You Only Live Twice", 1967)]
    [InlineData("\"007: Живёшь только дважды\"", 1967, "007: Живёшь только дважды", 1967)]
    [InlineData("1917", 2019, "1917", 2019)]
    [InlineData("2012", 2009, "2012", 2009)]
    public void Parse_PreservesNumericTitlesAndExtractsDecorativeYear(
        string rawName,
        int? suppliedYear,
        string expectedName,
        int expectedYear)
    {
        var lookup = MovieLookup.Parse(rawName, suppliedYear);

        Assert.Equal(expectedName, lookup.Name);
        Assert.Equal(expectedYear, lookup.Year);
    }

    [Fact]
    public void ExtractTmdbId_IsCaseInsensitive()
    {
        var providerIds = new Dictionary<string, string>
        {
            ["TMDB"] = "667"
        };

        Assert.Equal(667, MovieLookup.ExtractTmdbId(providerIds));
    }

    [Fact]
    public void SelectCandidate_PrefersExactTitleAndYear()
    {
        var candidates = new List<TmdbSearchItem>
        {
            new() { Id = 999, Title = "Дважды", ReleaseDate = "1967-01-01" },
            new()
            {
                Id = 667,
                Title = "007: Живёшь только дважды",
                OriginalTitle = "You Only Live Twice",
                ReleaseDate = "1967-06-13"
            },
            new()
            {
                Id = 123,
                Title = "You Only Live Twice",
                ReleaseDate = "1990-01-01"
            }
        };

        var selected = MovieLookup.SelectCandidate(
            candidates,
            new MovieLookup("You Only Live Twice", 1967));

        Assert.Equal(667, selected?.Id);
    }

    [Fact]
    public void TmdbMovieDetails_DeserializesImdbId()
    {
        const string json = """{"id":667,"imdb_id":"tt0062512"}""";

        var details = System.Text.Json.JsonSerializer.Deserialize<TmdbMovieDetails>(
            json,
            JsonOptions.Default);

        Assert.Equal("tt0062512", details?.ImdbId);
    }
}
