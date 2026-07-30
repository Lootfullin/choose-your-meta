using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using Xunit;

namespace RussianMetadata.Tests;

public sealed class SeriesSeasonMetadataTests
{
    [Fact]
    public void SeasonProvider_HandlesRemoteAndPostRefreshMetadata()
    {
        Assert.True(
            typeof(IRemoteMetadataProvider<Season, SeasonInfo>)
                .IsAssignableFrom(typeof(RussianSeasonProvider)));
        Assert.True(
            typeof(ICustomMetadataProvider<Season>)
                .IsAssignableFrom(typeof(RussianSeasonProvider)));
    }

    [Fact]
    public void SeriesDetails_DeserializesGenresAndCredits()
    {
        const string json = """
            {
              "id": 1399,
              "name": "Игра престолов",
              "genres": [{ "id": 18, "name": "драма" }],
              "credits": {
                "cast": [{
                  "id": 48,
                  "name": "Sean Bean",
                  "character": "Eddard Stark",
                  "order": 0
                }]
              }
            }
            """;

        var details = JsonSerializer.Deserialize<SeriesTmdbTvDetails>(
            json,
            JsonOptions.Default);

        Assert.Equal("драма", Assert.Single(details!.Genres!).Name);
        Assert.Equal(48, Assert.Single(details.Credits!.Cast!).Id);
    }

    [Fact]
    public void SeasonDetails_DeserializesRussianDataAndCredits()
    {
        const string json = """
            {
              "id": 3624,
              "name": "Сезон 1",
              "overview": "Первый сезон сериала.",
              "air_date": "2011-04-17",
              "credits": {
                "cast": [{
                  "id": 48,
                  "name": "Sean Bean",
                  "character": "Eddard Stark",
                  "order": 0
                }]
              }
            }
            """;

        var details = JsonSerializer.Deserialize<TmdbSeasonDetails>(
            json,
            JsonOptions.Default);

        Assert.Equal("Сезон 1", details?.Name);
        Assert.Equal("2011-04-17", details?.AirDate);
        Assert.Equal(48, Assert.Single(details!.Credits!.Cast!).Id);
    }

    [Fact]
    public void CanonicalPersonMapping_UsesSameRussianNameAndTmdbId()
    {
        var credits = new TmdbCredits
        {
            Cast =
            [
                new TmdbCastMember
                {
                    Id = 738,
                    Name = "Sean Connery",
                    Character = "James Bond",
                    Order = 0
                }
            ]
        };
        var russianNames = new Dictionary<int, string>
        {
            [738] = "Шон Коннери"
        };

        var person = Assert.Single(
            TmdbPeopleLocalization.CreateLocalizedPeople(
                credits,
                russianNames));

        Assert.Equal("Шон Коннери", person.Name);
        Assert.Equal("738", person.ProviderIds["Tmdb"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, true)]
    [InlineData(HttpStatusCode.BadGateway, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, true)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public void PersonLookup_RetriesOnlyTransientHttpFailures(
        HttpStatusCode statusCode,
        bool expected)
    {
        Assert.Equal(
            expected,
            TmdbPeopleLocalization.ShouldRetry(statusCode));
    }
}
