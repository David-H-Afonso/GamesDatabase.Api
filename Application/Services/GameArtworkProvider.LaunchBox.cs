using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using HtmlAgilityPack;

namespace GamesDatabase.Api.Application.Services;

public sealed partial class GameArtworkProvider
{
    private const string LaunchBoxOrigin = "https://gamesdb.launchbox-app.com";
    private static string NodeText(HtmlNode node) => string.Join(' ', HtmlEntity.DeEntitize(node.InnerText).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    private static HtmlDocument Document(string html) { var document = new HtmlDocument(); document.LoadHtml(html); return document; }
    private static int? LaunchBoxYear(string value) => MetadataEditionMatching.Year(GameDateNormalizer.NormalizeSteamReleaseDate(value))
        ?? (Regex.IsMatch(value, "^[12][0-9]{3}$") ? MetadataEditionMatching.Year(value) : null);
    private static bool AdvertisedRole(string alt, string role)
    {
        var size = Regex.Match(alt, @" - (?<width>[1-9][0-9]{1,4})x(?<height>[1-9][0-9]{1,4})$");
        if (!size.Success) return true;
        var width = int.Parse(size.Groups["width"].Value, CultureInfo.InvariantCulture); var height = int.Parse(size.Groups["height"].Value, CultureInfo.InvariantCulture);
        var ratio = (double)width / height;
        return (long)width * height <= 20_000_000 && (role switch { "cover" => width >= 200 && ratio is >= 0.45 and <= 0.95, "hero" => width >= 600 && ratio >= 1.4, _ => true });
    }

    private static Dictionary<string, string> FullLaunchBoxImages(HtmlNode page)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (page.SelectSingleNode("//script[@id='__NUXT_DATA__' and @type='application/json']") is not { } script) return result;
        using var data = JsonDocument.Parse(script.InnerText);
        var values = data.RootElement;
        if (values.ValueKind != JsonValueKind.Array) return result;
        string? Value(JsonElement item) => item.ValueKind == JsonValueKind.String ? item.GetString()
            : item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var index) && index >= 0 && index < values.GetArrayLength() && values[index].ValueKind == JsonValueKind.String ? values[index].GetString() : null;
        foreach (var item in values.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.Object))
        {
            if (!item.TryGetProperty("imageFileName", out var thumbnail) || !item.TryGetProperty("fullGameImageFileName", out var original) || Value(thumbnail) is not { } from || Value(original) is not { } to) continue;
            // The thumbnail is later bound to the selected game's labelled DOM
            // image. Related games elsewhere in page state cannot supply artwork.
            var url = (to.StartsWith("r2_", StringComparison.Ordinal) ? "https://gamesdb-images.launchbox.gg/" : "https://images.launchbox-app.com/") + to;
            if (PublicArtworkUrl.Valid(url)) result[from] = url;
        }
        return result;
    }

    private async Task<PublicGameMetadata?> LaunchBoxAsync(GameMetadataIdentityDto edition, HashSet<string> missing, GameArtworkLookup lookup, int? selectedId, CancellationToken token, bool refresh)
    {
        // The public route canonicalizes names to lower case with a 301. Request
        // its canonical form directly; automatic redirects remain disabled.
        var html = await HtmlAsync(LaunchBoxOrigin + "/games/results/" + Uri.EscapeDataString(edition.Name.ToLowerInvariant()), token, refresh);
        if (html is null) return null;
        var cards = Document(html).DocumentNode.SelectNodes("//*[@id='cardsContainer']//a[contains(concat(' ', normalize-space(@class), ' '), ' list-item ')]");
        if (cards is null || !cards.Any(card => card.SelectSingleNode(".//h3") is { } title && MetadataEditionMatching.SameTitle(NodeText(title), edition.Name)))
        {
            var discovery = MetadataNameMatching.Normalize(edition.Name);
            if (discovery.Length > 0 && !discovery.Equals(edition.Name.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                var fallback = await HtmlAsync(LaunchBoxOrigin + "/games/results/" + Uri.EscapeDataString(discovery), token, refresh);
                if (fallback is not null) cards = Document(fallback).DocumentNode.SelectNodes("//*[@id='cardsContainer']//a[contains(concat(' ', normalize-space(@class), ' '), ' list-item ')]");
            }
        }
        if (cards is null) return null;
        var matches = new List<GameMetadataIdentityDto>();
        foreach (var card in cards.Take(100))
        {
            var path = card.GetAttributeValue("href", "");
            var match = Regex.Match(path, @"^/games/details/(?<id>[1-9][0-9]{0,8})-[a-z0-9-]+$");
            var title = card.SelectSingleNode(".//h3") is { } heading ? NodeText(heading) : "";
            if (!match.Success || title.Length is < 1 or > 250 || !MetadataEditionMatching.SameTitle(title, edition.Name)) continue;
            var platform = card.SelectSingleNode(".//*[contains(concat(' ',normalize-space(@class),' '),' cardTitle ')]/p");
            var release = card.SelectSingleNode(".//*[contains(concat(' ',normalize-space(@class),' '),' releaseDate ')]/h5");
            var identity = new GameMetadataIdentityDto { Source = "launchbox", Name = title, Url = LaunchBoxOrigin + path,
                LaunchBoxId = int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture), Platforms = platform is null ? [] : [NodeText(platform)],
                ReleaseYear = release is null ? null : LaunchBoxYear(NodeText(release)) };
            if (!MetadataEditionMatching.Conflicts(identity, edition)) matches.Add(identity);
        }
        matches = matches.DistinctBy(identity => identity.LaunchBoxId).Take(10).ToList();
        lookup.Matches.AddRange(matches);
        var selected = selectedId.HasValue ? matches.SingleOrDefault(identity => identity.LaunchBoxId == selectedId) : MetadataEditionMatching.Best(matches, edition.Name, identity => identity, edition);
        if (selected is null) return null;
        html = await HtmlAsync(selected.Url, token, refresh);
        if (html is null) return null;
        var page = Document(html).DocumentNode;
        if (page.SelectSingleNode("//h1") is not { } h1 || !MetadataEditionMatching.SameTitle(NodeText(h1), selected.Name)) return null;
        var detailPlatforms = page.SelectNodes("//dt")?.Where(node => NodeText(node) == "Platform").Select(node => node.SelectSingleNode("following-sibling::dd[1]")).Where(node => node is not null).Select(node => NodeText(node!)).Distinct().ToList() ?? [];
        var releaseNode = page.SelectNodes("//dt")?.FirstOrDefault(node => NodeText(node) == "Release Date")?.SelectSingleNode("following-sibling::dd[1]");
        var year = releaseNode is null ? null : LaunchBoxYear(NodeText(releaseNode));
        var verified = new GameMetadataIdentityDto { Source = "launchbox", LaunchBoxId = selected.LaunchBoxId, Name = selected.Name, Url = selected.Url, ReleaseYear = year, Platforms = detailPlatforms };
        // Search snippets are discovery only. Both year and platform are checked
        // again on the selected game's public page before considering its images.
        if (MetadataEditionMatching.Conflicts(verified, edition) || MetadataEditionMatching.Conflicts(verified, selected)
            || edition.ReleaseYear.HasValue && !year.HasValue || selected.Platforms.Count > 0 && detailPlatforms.Count == 0) return null;
        var result = new PublicGameMetadata { Identity = verified };
        var originals = FullLaunchBoxImages(page);
        var images = page.SelectNodes("//img[@src and @alt]");
        if (images is null) return result;
        foreach (var (role, label) in new[] { ("logo", "Clear Logo"), ("hero", "Fanart - Background"), ("hero", "Banner"), ("hero", "Screenshot - Gameplay"), ("hero", "Screenshot - Game Title"), ("cover", "Box - Front") }.Where(pair => missing.Contains(pair.Item1)))
        {
            if (result.Fields.ContainsKey(role)) continue;
            var candidates = images.Select(image => (Node: image, Alt: HtmlEntity.DeEntitize(image.GetAttributeValue("alt", ""))))
                .Where(image => image.Alt.StartsWith(selected.Name + " - " + label + " (", StringComparison.OrdinalIgnoreCase))
                .Where(image => AdvertisedRole(image.Alt, role))
                .OrderBy(image => image.Alt.Contains("(North America)", StringComparison.Ordinal) ? 0 : image.Alt.Contains("(World)", StringComparison.Ordinal) ? 1 : 2).Take(3);
            foreach (var candidate in candidates)
            {
                var url = HtmlEntity.DeEntitize(candidate.Node.GetAttributeValue("src", ""));
                if (PublicArtworkUrl.Valid(url) && originals.TryGetValue(new Uri(url).Segments.Last(), out var original)) url = original;
                if (PublicArtworkUrl.Valid(url) && new Uri(url) is { Host: "images.launchbox-app.com" } legacy) url = "https://images.launchbox-app.com/" + legacy.AbsolutePath.TrimStart('/');
                if (!PublicArtworkUrl.Valid(url) || !await VerifyAsync(url, role, lookup, token, refresh)) continue;
                result.Fields[role] = url;
                break;
            }
        }
        return result;
    }
}
