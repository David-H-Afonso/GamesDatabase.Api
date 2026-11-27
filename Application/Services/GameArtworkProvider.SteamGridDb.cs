using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Services;

public sealed partial class GameArtworkProvider
{
    private async Task<PublicGameMetadata?> SteamGridDbAsync(GameMetadataIdentityDto edition, HashSet<string> missing, GameArtworkLookup lookup, CancellationToken token, bool refresh)
    {
        var key = settings.Value.SteamGridDbApiKey;
        // The documented API requires an owner's key. Never scrape a session key
        // from their website or ship a shared credential to the frontend.
        if (key is null || !Regex.IsMatch(key, "^[A-Za-z0-9_-]{16,128}$")) return null;
        const string origin = "https://www.steamgriddb.com/api/v2";
        var path = edition.SteamAppId.HasValue ? "/games/steam/" + edition.SteamAppId.Value.ToString(CultureInfo.InvariantCulture) : "/search/autocomplete/" + Uri.EscapeDataString(edition.Name);
        using var search = await JsonAsync(origin + path, token, key);
        if (search is null || search.RootElement.ValueKind != JsonValueKind.Object || !search.RootElement.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True || !search.RootElement.TryGetProperty("data", out var data)) throw new JsonException("Invalid SteamGridDB search response.");
        var rows = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().Take(15).ToList() : data.ValueKind == JsonValueKind.Object ? [data] : [];
        var candidates = new List<(int Id, GameMetadataIdentityDto Identity)>();
        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt32(out var number) || number <= 0 || Text(row, "name") is not { Length: > 0 and <= 250 } name) continue;
            int? year = null;
            if (row.TryGetProperty("release_date", out var released) && released.ValueKind == JsonValueKind.Number && released.TryGetInt64(out var seconds) && seconds is >= -631152000 and <= 7289654400) year = DateTimeOffset.FromUnixTimeSeconds(seconds).Year;
            if (!edition.SteamAppId.HasValue && (edition.ReleaseYear.HasValue && year != edition.ReleaseYear || !MetadataEditionMatching.SameTitle(name, edition.Name))) continue;
            candidates.Add((number, new() { Source = "steamgriddb", Name = name, Url = "https://www.steamgriddb.com/game/" + number.ToString(CultureInfo.InvariantCulture), ReleaseYear = year }));
        }
        if (candidates.Count != 1) return null;
        var selected = candidates[0];
        var result = new PublicGameMetadata { Identity = selected.Identity };
        foreach (var (role, resource) in new[] { ("logo", "logos"), ("hero", "heroes"), ("cover", "grids") }.Where(pair => missing.Contains(pair.Item1)))
        {
            var filters = "?types=static&nsfw=false&humor=false&epilepsy=false&mimes=image%2Fpng,image%2Fjpeg,image%2Fwebp&limit=5" + (role == "cover" ? "&dimensions=600x900,342x482,660x930" : role == "logo" ? "&styles=official,white" : "");
            try
            {
                using var assets = await JsonAsync(origin + "/" + resource + "/game/" + selected.Id.ToString(CultureInfo.InvariantCulture) + filters, token, key);
                if (assets is null || assets.RootElement.ValueKind != JsonValueKind.Object || !assets.RootElement.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True || !assets.RootElement.TryGetProperty("data", out var items) || items.ValueKind != JsonValueKind.Array) throw new JsonException("Invalid SteamGridDB artwork response.");
                foreach (var asset in items.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object).Take(5))
                {
                    if (new[] { "nsfw", "humor", "epilepsy" }.Any(flag => asset.TryGetProperty(flag, out var value) && value.ValueKind == JsonValueKind.True)) continue;
                    var url = Text(asset, "url");
                    if (url is null || !PublicArtworkUrl.Valid(url) || new Uri(url).Host is not ("cdn.steamgriddb.com" or "cdn2.steamgriddb.com") || !await VerifyAsync(url, role, lookup, token, refresh)) continue;
                    result.Fields[role] = url;
                    break;
                }
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !token.IsCancellationRequested) { Failed(lookup, error); }
        }
        return result;
    }
}
