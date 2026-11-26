using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace GamesDatabase.Api.Application.Services;

/// <summary>Public facts only. Steam first, CC0 Wikidata second; no player sync or app-cache writes.</summary>
public sealed class PublicGameMetadataProvider(IHttpClientFactory clients, ISteamStoreService steam, ISteamArtworkService artwork, IMemoryCache cache, TimeProvider clock) : IPublicGameMetadataProvider
{
    private readonly HttpClient client = clients.CreateClient("game-metadata");
    private static string? Text(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private async Task<JsonDocument?> ReadAsync(string url, CancellationToken cancellationToken)
    {
        var host = new Uri(url).Host;
        if (cache.TryGetValue<DateTimeOffset>("metadata-cooldown:" + host, out var until) && until > clock.GetUtcNow()) return null;
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode == 429)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - clock.GetUtcNow() : TimeSpan.FromMinutes(1));
            delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 3600));
            cache.Set("metadata-cooldown:" + host, clock.GetUtcNow() + delay, delay);
            return null;
        }
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1_000_000) return null;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(); var block = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + count > 1_000_000) return null;
            buffer.Write(block, 0, count);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }

    public async Task<List<GameMetadataIdentityDto>> SearchSteamAsync(string name, CancellationToken cancellationToken)
    {
        using var data = await ReadAsync("https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(name) + "&l=english&cc=US", cancellationToken);
        if (data is null || !data.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return [];
        var result = new List<GameMetadataIdentityDto>();
        foreach (var item in items.EnumerateArray().Take(6))
            if (item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var appId) && appId > 0 && Text(item, "name") is { Length: > 0 } title)
                result.Add(new() { Source = "steam", SteamAppId = appId, Name = title, Url = $"https://store.steampowered.com/app/{appId}/" });
        return result;
    }

    internal static bool SteamImage(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".steamusercontent.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".akamaihd.net", StringComparison.OrdinalIgnoreCase));

    public async Task<PublicGameMetadata?> SteamAsync(int appId, bool includeArtwork, CancellationToken cancellationToken)
    {
        var details = await steam.GetAppDetailsAsync(appId, cancellationToken);
        if (details is null || string.IsNullOrWhiteSpace(details.Name)) return null;
        var result = new PublicGameMetadata { Identity = new() { Source = "steam", SteamAppId = appId, Name = details.Name, Url = $"https://store.steampowered.com/app/{appId}/" } };
        if (details.Name.Length <= 250) result.Fields["name"] = details.Name.Trim();
        if (details.Developer?.Trim() is { Length: > 0 and <= 150 } developer) result.Fields["developer"] = developer;
        if (GameDateNormalizer.NormalizeSteamReleaseDate(details.ReleaseDate) is { } date) result.Fields["released"] = date;
        if (details.MetacriticScore is >= 0 and <= 100) result.Fields["critic"] = details.MetacriticScore.Value;
        if (includeArtwork)
        {
            try
            {
                var assets = await artwork.GetAsync(appId, cancellationToken);
                if (SteamImage(assets?.LogoUrl)) result.Fields["logo"] = assets!.LogoUrl!;
                if (SteamImage(assets?.HeroUrl)) result.Fields["hero"] = assets!.HeroUrl!;
                if (SteamImage(assets?.CoverUrl)) result.Fields["cover"] = assets!.CoverUrl!;
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException)
            {
                // Optional artwork availability must not discard already verified store facts.
                // Request cancellation is still enforced before returning any facts.
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static string? Binding(JsonElement row, string key) => row.TryGetProperty(key, out var value) ? Text(value, "value") : null;
    public async Task<List<PublicGameMetadata>> WikidataAsync(string name, int? appId, CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        if (!appId.HasValue)
        {
            using var found = await ReadAsync("https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&type=item&language=en&limit=5&maxlag=5&search=" + Uri.EscapeDataString(name), cancellationToken);
            if (found is null || !found.RootElement.TryGetProperty("search", out var matches) || matches.ValueKind != JsonValueKind.Array) return [];
            ids = matches.EnumerateArray().Select(item => Text(item, "id")).Where(id => id is not null && Regex.IsMatch(id, "^Q[1-9][0-9]{0,14}$")).Cast<string>().Distinct().Take(5).ToList();
            if (ids.Count == 0) return [];
        }
        // Only validated numeric IDs enter SPARQL; names are sent to the search API, never interpolated as expressions.
        var identity = appId.HasValue ? $"?item wdt:P1733 \"{appId.Value.ToString(CultureInfo.InvariantCulture)}\" ." : "VALUES ?item { " + string.Join(" ", ids.Select(id => "wd:" + id)) + " }";
        var query = "PREFIX wd: <http://www.wikidata.org/entity/> PREFIX wdt: <http://www.wikidata.org/prop/direct/> PREFIX p: <http://www.wikidata.org/prop/> PREFIX psv: <http://www.wikidata.org/prop/statement/value/> PREFIX wikibase: <http://wikiba.se/ontology#> PREFIX bd: <http://www.bigdata.com/rdf#> SELECT ?item ?itemLabel ?developerLabel ?date ?precision WHERE { " + identity +
            " VALUES ?gameType { wd:Q7889 wd:Q209163 wd:Q16070115 } ?item wdt:P31/wdt:P279* ?gameType . " +
            "OPTIONAL { ?item wdt:P178 ?developer . } OPTIONAL { ?item p:P577/psv:P577 ?published . ?published wikibase:timeValue ?date; wikibase:timePrecision ?precision . } " +
            "SERVICE wikibase:label { bd:serviceParam wikibase:language \"en\" . } } LIMIT 100";
        using var data = await ReadAsync("https://query.wikidata.org/sparql?format=json&query=" + Uri.EscapeDataString(query), cancellationToken);
        if (data is null || !data.RootElement.TryGetProperty("results", out var results) || !results.TryGetProperty("bindings", out var bindings) || bindings.ValueKind != JsonValueKind.Array) return [];
        var valid = bindings.EnumerateArray().Where(row => Binding(row, "item") is { } value && Regex.IsMatch(value, "^https?://www\\.wikidata\\.org/entity/Q[1-9][0-9]{0,14}$")).ToList();
        var output = new List<PublicGameMetadata>();
        foreach (var group in valid.GroupBy(row => Binding(row, "item")!.Split('/').Last()).Take(5))
        {
            var title = Binding(group.First(), "itemLabel");
            if (string.IsNullOrWhiteSpace(title) || title == group.Key || title.Length > 250) continue;
            var result = new PublicGameMetadata { Identity = new() { Source = "wikidata", WikidataId = group.Key, Name = title, Url = $"https://www.wikidata.org/wiki/{group.Key}" } };
            result.Fields["name"] = title;
            var developers = group.Select(row => Binding(row, "developerLabel")).Where(value => !string.IsNullOrWhiteSpace(value) && !Regex.IsMatch(value!, "^Q[0-9]+$")).Distinct().Order(StringComparer.Ordinal).ToList();
            var developer = string.Join(", ", developers); if (developer.Length is > 0 and <= 150) result.Fields["developer"] = developer;
            var dates = group.Where(row => int.TryParse(Binding(row, "precision"), out var precision) && precision >= 11).Select(row => Binding(row, "date")).Where(date => date?.Length >= 10).Select(date => GameDateNormalizer.NormalizeSteamReleaseDate(date![..10])).Where(date => date is not null).Distinct().ToList();
            // Multiple regional/platform release dates are ambiguous, not permission to invent a preferred one.
            if (dates.Count == 1) result.Fields["released"] = dates[0]!;
            output.Add(result);
        }
        return output;
    }
}
