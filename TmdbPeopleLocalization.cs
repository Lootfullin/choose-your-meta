using System.Globalization;
using System.Text.Json;
using System.Collections.Concurrent;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace RussianMetadata;

internal static class TmdbPeopleLocalization
{
    private const string WikidataSparqlEndpoint =
        "https://query.wikidata.org/sparql";
    private static readonly ConcurrentDictionary<int, string>
        RussianNameCache = new();

    public static async Task AddLocalizedPeople<TItem>(
        TmdbCredits credits,
        MetadataResult<TItem> result,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
        where TItem : BaseItem
    {
        var cast = credits.Cast?
            .Where(person =>
                person.Id > 0
                && !string.IsNullOrWhiteSpace(person.Name))
            .OrderBy(person => person.Order)
            .Take(50)
            .ToArray() ?? [];
        var crew = credits.Crew?
            .Where(person =>
                person.Id > 0
                && !string.IsNullOrWhiteSpace(person.Name)
                && MovieTextLocalization.MapCrewJob(person.Job) is not null)
            .ToArray() ?? [];
        var ids = cast.Select(person => person.Id)
            .Concat(crew.Select(person => person.Id))
            .Distinct()
            .ToArray();
        var russianNames = await FetchRussianLabels(
            ids,
            httpClientFactory,
            logger,
            cancellationToken);

        foreach (var person in CreateLocalizedPeople(
            new TmdbCredits
            {
                Cast = [.. cast],
                Crew = [.. crew]
            },
            russianNames))
        {
            result.AddPerson(person);
        }
    }

    internal static IReadOnlyList<PersonInfo> CreateLocalizedPeople(
        TmdbCredits credits,
        IReadOnlyDictionary<int, string> russianNames)
    {
        var people = new List<PersonInfo>();
        foreach (var actor in credits.Cast?
            .Where(person =>
                person.Id > 0
                && !string.IsNullOrWhiteSpace(person.Name))
            .OrderBy(person => person.Order)
            .Take(50) ?? [])
        {
            var person = new PersonInfo
            {
                Name = russianNames.GetValueOrDefault(actor.Id)
                    ?? actor.Name!.Trim(),
                Role = actor.Character?.Trim() ?? string.Empty,
                Type = PersonKind.Actor,
                SortOrder = actor.Order,
                ImageUrl = BuildProfileUrl(actor.ProfilePath)
            };
            person.SetProviderId(
                "Tmdb",
                actor.Id.ToString(CultureInfo.InvariantCulture));
            people.Add(person);
        }

        foreach (var member in credits.Crew?
            .Where(person =>
                person.Id > 0
                && !string.IsNullOrWhiteSpace(person.Name)
                && MovieTextLocalization.MapCrewJob(person.Job) is not null)
            ?? [])
        {
            var kind = MovieTextLocalization.MapCrewJob(member.Job);
            if (kind is null)
            {
                continue;
            }

            var person = new PersonInfo
            {
                Name = russianNames.GetValueOrDefault(member.Id)
                    ?? member.Name!.Trim(),
                Role = member.Job?.Trim() ?? string.Empty,
                Type = kind.Value,
                ImageUrl = BuildProfileUrl(member.ProfilePath)
            };
            person.SetProviderId(
                "Tmdb",
                member.Id.ToString(CultureInfo.InvariantCulture));
            people.Add(person);
        }

        return people;
    }

    private static async Task<Dictionary<int, string>> FetchRussianLabels(
        IReadOnlyCollection<int> ids,
        IHttpClientFactory httpClientFactory,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var pendingIds = ids
            .Distinct()
            .Where(id => !RussianNameCache.ContainsKey(id))
            .ToArray();
        if (pendingIds.Length == 0)
        {
            return CachedRussianNames(ids);
        }

        using var httpClient = httpClientFactory.CreateClient(
            "RussianMetadata");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "Accept",
            "application/json");
        httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent",
            "ChooseYourMeta/1.4");
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var values = string.Join(
            " ",
            pendingIds.Select(id =>
                $"\"{id.ToString(CultureInfo.InvariantCulture)}\""));
        var query = $@"
SELECT ?externalId ?ruLabel WHERE {{
  VALUES ?externalId {{ {values} }}
  ?item wdt:P4985 ?externalId.
  ?item rdfs:label ?ruLabel.
  FILTER(LANG(?ruLabel) = ""ru"")
}}";
        var url =
            $"{WikidataSparqlEndpoint}?format=json&query={Uri.EscapeDataString(query)}";

        try
        {
            using var response = await httpClient.GetAsync(
                url,
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return CachedRussianNames(ids);
            }

            var json = await response.Content.ReadAsStringAsync(
                cancellationToken);
            var data = JsonSerializer.Deserialize<SparqlResult>(
                json,
                JsonOptions.Default);
            var fetched = data?.Results?.Bindings?
                .Where(binding =>
                    int.TryParse(
                        binding.ExternalId?.Value,
                        NumberStyles.None,
                        CultureInfo.InvariantCulture,
                        out _)
                    && !string.IsNullOrWhiteSpace(binding.RuLabel?.Value))
                .GroupBy(binding => binding.ExternalId!.Value!)
                .ToDictionary(
                    group => int.Parse(
                        group.Key,
                        CultureInfo.InvariantCulture),
                    group => group.First().RuLabel!.Value!)
                ?? [];
            foreach (var id in pendingIds)
            {
                RussianNameCache.TryAdd(
                    id,
                    fetched.GetValueOrDefault(id) ?? string.Empty);
            }

            return CachedRussianNames(ids);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(
                ex,
                "ChooseYourMeta: Russian person label lookup failed");
            return CachedRussianNames(ids);
        }
    }

    private static Dictionary<int, string> CachedRussianNames(
        IEnumerable<int> ids)
    {
        return ids
            .Distinct()
            .Where(id =>
                RussianNameCache.TryGetValue(id, out var value)
                && !string.IsNullOrWhiteSpace(value))
            .ToDictionary(id => id, id => RussianNameCache[id]);
    }

    private static string? BuildProfileUrl(string? profilePath)
    {
        return string.IsNullOrWhiteSpace(profilePath)
            ? null
            : $"https://image.tmdb.org/t/p/original{profilePath}";
    }
}
