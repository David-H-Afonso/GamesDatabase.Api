using System.Text.Json;
using System.Text.RegularExpressions;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using Microsoft.Extensions.Caching.Memory;

namespace GamesDatabase.Api.Application.Services;

/// <summary>Public facts only. Bounded Wikidata entity JSON avoids the fragile SPARQL join.</summary>
public sealed class PublicGameMetadataProvider(IHttpClientFactory clients, ISteamStoreService steam, ISteamArtworkService artwork, IMemoryCache cache, TimeProvider clock) : IPublicGameMetadataProvider
{
    private readonly HttpClient client = clients.CreateClient("game-metadata");
    private static string? Text(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private async Task<JsonDocument?> ReadAsync(string url, CancellationToken cancellationToken)
    {
        var host = new Uri(url).Host;
        if (cache.TryGetValue<DateTimeOffset>("metadata-cooldown:" + host, out var until) && until > clock.GetUtcNow())
            throw new MetadataSourceUnavailableException(host, (int)Math.Ceiling((until - clock.GetUtcNow()).TotalSeconds));
        using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if ((int)response.StatusCode is 403 or 429 or >= 500)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - clock.GetUtcNow() : TimeSpan.FromMinutes(1));
            delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 3600));
            cache.Set("metadata-cooldown:" + host, clock.GetUtcNow() + delay, delay);
            throw new MetadataSourceUnavailableException(host, (int)Math.Ceiling(delay.TotalSeconds));
        }
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > 1_000_000) throw new MetadataSourceUnavailableException(host);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream(); var block = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(block, cancellationToken)) > 0)
        {
            if (buffer.Length + count > 1_000_000) throw new MetadataSourceUnavailableException(host);
            buffer.Write(block, 0, count);
        }
        var data = JsonDocument.Parse(buffer.ToArray());
        if (data.RootElement.ValueKind != JsonValueKind.Object) { data.Dispose(); throw new JsonException("Invalid public metadata response."); }
        if (data.RootElement.TryGetProperty("error", out _))
        {
            data.Dispose();
            cache.Set("metadata-cooldown:" + host, clock.GetUtcNow().AddMinutes(1), TimeSpan.FromMinutes(1));
            throw new MetadataSourceUnavailableException(host, 60);
        }
        return data;
    }

    public async Task<List<GameMetadataIdentityDto>> SearchSteamAsync(string name, CancellationToken cancellationToken)
    {
        using var data = await ReadAsync("https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(name) + "&l=english&cc=US", cancellationToken);
        if (data is null || !data.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) throw new MetadataSourceUnavailableException("Steam");
        var result = new List<GameMetadataIdentityDto>();
        foreach (var item in items.EnumerateArray().Take(6))
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var appId) && appId > 0 && Text(item, "name") is { Length: > 0 } title)
                result.Add(new() { Source = "steam", SteamAppId = appId, Name = title, Platforms = ["PC"], Url = $"https://store.steampowered.com/app/{appId}/" });
        return result;
    }

    internal static bool SteamImage(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo)
        && (uri.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".steamusercontent.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".akamaihd.net", StringComparison.OrdinalIgnoreCase));

    public async Task<PublicGameMetadata?> SteamAsync(int appId, bool includeArtwork, CancellationToken cancellationToken)
    {
        var details = await steam.GetAppDetailsAsync(appId, cancellationToken);
        if (details is null || string.IsNullOrWhiteSpace(details.Name)) return null;
        var result = new PublicGameMetadata { Identity = new() { Source = "steam", SteamAppId = appId, Name = details.Name, Platforms = ["PC"], Url = $"https://store.steampowered.com/app/{appId}/" } };
        if (details.Name.Length <= 250) result.Fields["name"] = details.Name.Trim();
        if (details.Developer?.Trim() is { Length: > 0 and <= 150 } developer) result.Fields["developer"] = developer;
        if (GameDateNormalizer.NormalizeSteamReleaseDate(details.ReleaseDate) is { } date) { result.Fields["released"] = date; result.Identity.ReleaseYear = MetadataEditionMatching.Year(date); }
        if (details.MetacriticScore is >= 0 and <= 100) { result.Fields["critic"] = details.MetacriticScore.Value; result.CriticProvider = "Metacritic"; result.CriticScope = "steam-edition"; }
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

    private static bool EntityId(string? id) => id is not null && Regex.IsMatch(id, "^Q[1-9][0-9]{0,14}$");
    private static string? Label(JsonElement entity) => entity.TryGetProperty("labels", out var labels)
        ? new[] { "en", "mul" }.Select(language => labels.TryGetProperty(language, out var label) ? Text(label, "value") : null).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) : null;
    private static IEnumerable<JsonElement> Claims(JsonElement entity, string property) =>
        entity.ValueKind == JsonValueKind.Object && entity.TryGetProperty("claims", out var claims) && claims.ValueKind == JsonValueKind.Object && claims.TryGetProperty(property, out var values) && values.ValueKind == JsonValueKind.Array
        ? values.EnumerateArray().Where(claim => claim.ValueKind == JsonValueKind.Object && Text(claim, "rank") != "deprecated" && claim.TryGetProperty("mainsnak", out var snak) && snak.ValueKind == JsonValueKind.Object && snak.TryGetProperty("datavalue", out var data) && data.ValueKind == JsonValueKind.Object && data.TryGetProperty("value", out _)).Select(claim => claim.GetProperty("mainsnak").GetProperty("datavalue").GetProperty("value")) : [];
    private static List<string> ClaimIds(JsonElement entity, string property) => Claims(entity, property).Where(value => value.ValueKind == JsonValueKind.Object).Select(value => Text(value, "id")).Where(EntityId).Cast<string>().Distinct().Take(20).ToList();
    private static List<string> Aliases(JsonElement entity) => entity.TryGetProperty("aliases", out var aliases) && aliases.ValueKind == JsonValueKind.Object
        ? new[] { "en", "mul" }.SelectMany(language => aliases.TryGetProperty(language, out var values) && values.ValueKind == JsonValueKind.Array ? values.EnumerateArray().ToList() : [])
            .Select(value => Text(value, "value")).Where(value => value is { Length: > 0 and <= 250 }).Cast<string>().Distinct().Take(15).ToList() : [];

    private async Task<HashSet<string>> GameTypesAsync(IEnumerable<string> types, CancellationToken token)
    {
        var known = new HashSet<string>(StringComparer.Ordinal) { "Q7889", "Q209163", "Q16070115" };
        var roots = types.Distinct().Take(20).ToList();
        var parents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var frontier = roots.Where(id => !known.Contains(id)).ToList();
        for (var depth = 0; depth < 3 && frontier.Count > 0; depth++)
        {
            var read = new List<string>();
            foreach (var id in frontier.Where(id => !known.Contains(id) && !parents.ContainsKey(id)).Distinct().Take(20))
                if (cache.TryGetValue<bool>("wikidata-game-type:" + id, out var game)) { if (game) known.Add(id); else parents[id] = []; }
                else read.Add(id);
            if (read.Count == 0) break;
            using var data = await ReadAsync("https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=claims&maxlag=5&ids=" + Uri.EscapeDataString(string.Join('|', read)), token);
            if (data is null || !data.RootElement.TryGetProperty("entities", out var entities)) throw new MetadataSourceUnavailableException("Wikidata");
            foreach (var id in read) parents[id] = entities.TryGetProperty(id, out var entity) ? ClaimIds(entity, "P279") : [];
            frontier = read.SelectMany(id => parents[id]).Where(id => !known.Contains(id) && !parents.ContainsKey(id)).Distinct().Take(20).ToList();
        }
        bool IsGame(string id, HashSet<string> visited) => known.Contains(id) || visited.Add(id) && parents.TryGetValue(id, out var ancestors) && ancestors.Any(parent => IsGame(parent, visited));
        foreach (var id in roots)
        {
            var game = IsGame(id, []);
            if (game) known.Add(id);
            cache.Set("wikidata-game-type:" + id, game, TimeSpan.FromHours(6));
        }
        return known;
    }

    private async Task<List<(string Id, string? Label)>> SearchWikiAsync(string name, CancellationToken token)
    {
        using var data = await ReadAsync("https://www.wikidata.org/w/api.php?action=wbsearchentities&format=json&type=item&language=en&limit=10&maxlag=5&search=" + Uri.EscapeDataString(name), token);
        if (data is null || !data.RootElement.TryGetProperty("search", out var matches) || matches.ValueKind != JsonValueKind.Array) throw new MetadataSourceUnavailableException("Wikidata");
        return matches.EnumerateArray().Where(item => EntityId(Text(item, "id"))).Take(10).Select(item => (Text(item, "id")!, Text(item, "label"))).DistinctBy(item => item.Item1).ToList();
    }

    public async Task<List<PublicGameMetadata>> WikidataAsync(string name, int? appId, CancellationToken cancellationToken, string? selectedId = null, bool forceRefresh = false)
    {
        if (name.Length > 250 || appId is <= 0 || selectedId is not null && !EntityId(selectedId)) return [];
        var found = selectedId is not null ? new List<(string Id, string? Label)> { (selectedId, null) } : await SearchWikiAsync(name, cancellationToken);
        if (selectedId is null && !found.Any(item => item.Label is not null && MetadataEditionMatching.SameTitle(item.Label, name)))
        {
            var cosmetic = name.Replace('\u2019', '\'').Replace('\u2018', '\'').Replace('\u00a0', ' ');
            if (!cosmetic.Equals(name, StringComparison.Ordinal)) found = await SearchWikiAsync(cosmetic, cancellationToken);
        }
        if (selectedId is null && !found.Any(item => item.Label is not null && MetadataEditionMatching.SameTitle(item.Label, name)))
        {
            // A bounded keyword discovery retry repairs punctuation-sensitive upstream
            // searches. The FULL title must still match; a prefix is never an identity.
            var words = MetadataNameMatching.Normalize(name).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var discovery = string.Join(' ', words.Length > 2 ? words.Take(2) : words);
            if (discovery.Length > 0 && !discovery.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                var fallback = await SearchWikiAsync(discovery, cancellationToken);
                found = found.Concat(fallback).DistinctBy(item => item.Id).Take(15).ToList();
            }
        }
        if (found.Count == 0) return [];
        using var data = await ReadAsync("https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=labels%7Caliases%7Cclaims&languages=en%7Cmul&languagefallback=1&maxlag=5&ids=" + Uri.EscapeDataString(string.Join('|', found.Select(item => item.Id))), cancellationToken);
        if (data is null || !data.RootElement.TryGetProperty("entities", out var entities) || entities.ValueKind != JsonValueKind.Object) throw new MetadataSourceUnavailableException("Wikidata");
        var directTypes = new HashSet<string>(StringComparer.Ordinal) { "Q7889", "Q209163", "Q16070115" };
        var unknownTypes = found.Where(item => entities.TryGetProperty(item.Id, out var entity) && !ClaimIds(entity, "P31").Any(directTypes.Contains)
            && (selectedId is not null || appId.HasValue || Aliases(entity).Append(Label(entity) ?? item.Label ?? "").Any(title => MetadataEditionMatching.SameTitle(title, name))))
            .SelectMany(item => ClaimIds(entities.GetProperty(item.Id), "P31"));
        var gameTypes = await GameTypesAsync(unknownTypes, cancellationToken);
        var valid = found.Where(item => entities.TryGetProperty(item.Id, out var entity) && ClaimIds(entity, "P31").Any(gameTypes.Contains)
            && (!appId.HasValue || Claims(entity, "P1733").Any(value => value.ValueKind == JsonValueKind.String && value.GetString() == appId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)))).ToList();
        var labelIds = valid.SelectMany(item => ClaimIds(entities.GetProperty(item.Id), "P178").Concat(ClaimIds(entities.GetProperty(item.Id), "P400"))).Distinct().Take(40).ToList();
        var labels = new Dictionary<string, string>(); var labelsFailed = false;
        if (labelIds.Count > 0)
        {
            try
            {
                using var names = await ReadAsync("https://www.wikidata.org/w/api.php?action=wbgetentities&format=json&props=labels&languages=en%7Cmul&languagefallback=1&maxlag=5&ids=" + Uri.EscapeDataString(string.Join('|', labelIds)), cancellationToken);
                if (names is not null && names.RootElement.TryGetProperty("entities", out var values) && values.ValueKind == JsonValueKind.Object)
                    foreach (var id in labelIds)
                        if (values.TryGetProperty(id, out var entity) && Label(entity) is { Length: > 0 and <= 150 } label && !EntityId(label)) labels[id] = label;
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested) { labelsFailed = true; }
        }
        var output = new List<PublicGameMetadata>();
        foreach (var match in valid)
        {
            var entity = entities.GetProperty(match.Id); var aliases = Aliases(entity); var title = Label(entity) ?? match.Label;
            if (string.IsNullOrWhiteSpace(title) || EntityId(title) || title.Length > 250) continue;
            // Some catalog labels omit a brand (Wolverine); a full published alias
            // (Marvel's Wolverine) is identity evidence, not a fuzzy title guess.
            // Equivalent spellings inside ONE verified entity are not competing
            // games. Keep the provider's first canonical matching representation.
            title = new[] { title }.Concat(aliases).FirstOrDefault(value => MetadataEditionMatching.SameTitle(value, name)) ?? title;
            var published = Claims(entity, "P577").Where(value => value.ValueKind == JsonValueKind.Object && value.TryGetProperty("precision", out var precision) && precision.TryGetInt32(out var number) && number >= 9)
                .Select(value => (Time: Text(value, "time")?.TrimStart('+'), Precision: value.GetProperty("precision").GetInt32())).ToList();
            var years = published.Select(value => MetadataEditionMatching.Year(value.Time)).OfType<int>().ToList();
            var result = new PublicGameMetadata { Identity = new() { Source = "wikidata", WikidataId = match.Id, SteamAppId = appId, Name = title, Url = $"https://www.wikidata.org/wiki/{match.Id}", ReleaseYear = years.Count > 0 ? years.Min() : null,
                Platforms = ClaimIds(entity, "P400").Where(labels.ContainsKey).Select(id => labels[id]).ToList() } };
            result.Fields["name"] = title;
            result.Aliases = aliases;
            var developer = string.Join(", ", ClaimIds(entity, "P178").Where(labels.ContainsKey).Select(id => labels[id]).Distinct().Order(StringComparer.Ordinal));
            if (developer.Length is > 0 and <= 150) result.Fields["developer"] = developer;
            var dates = published.Where(value => value.Precision >= 11 && value.Time is { Length: >= 10 }).Select(value => GameDateNormalizer.NormalizeSteamReleaseDate(value.Time![..10])).Where(date => date is not null).Distinct().ToList();
            if (dates.Count == 1) result.Fields["released"] = dates[0]!;
            result.OfficialUrls = Claims(entity, "P856").Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).Where(url => url.Length <= 600).Take(5).ToList();
            result.CommonsLogo = Claims(entity, "P154").Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()).FirstOrDefault(value => value is { Length: > 0 and <= 250 });
            if (labelsFailed) result.Notices.Add("wikidataUnavailable");
            output.Add(result);
        }
        return output;
    }
}
