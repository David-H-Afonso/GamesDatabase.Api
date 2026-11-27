using System.Text.Json;
using System.Text.RegularExpressions;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using HtmlAgilityPack;

namespace GamesDatabase.Api.Application.Services;

public sealed partial class GameArtworkProvider
{
    private static bool NintendoPage(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && uri.Host == "www.nintendo.com" && Regex.IsMatch(uri.AbsolutePath, "^/us/store/products/[a-z0-9]+(?:-[a-z0-9]+)*/$");

    private static string? NintendoAsset(JsonElement image, string transform)
    {
        if (Text(image, "publicId") is not { Length: > 0 and <= 500 } path || path.Split('/').Any(part => part is "." or "..") || path.Contains('\\') || path.Contains('?') || path.Contains('#')) return null;
        return "https://assets.nintendo.com/image/upload/" + transform + "/" + string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
    }

    private async Task<PublicGameMetadata?> NintendoAsync(IReadOnlyList<PublicGameMetadata> evidence, GameMetadataIdentityDto edition, HashSet<string> missing, GameArtworkLookup lookup, CancellationToken token, bool refresh)
    {
        // Only a published official link from the confirmed edition is followed.
        // A 1998 title must never be turned into a guessed Switch 2 store slug.
        var url = evidence.SelectMany(source => source.OfficialUrls).FirstOrDefault(NintendoPage);
        if (url is null) return null;
        var html = await HtmlAsync(url, token, refresh);
        if (html is null) return null;
        var document = Document(html).DocumentNode;
        if (document.SelectSingleNode("//link[@rel='canonical']")?.GetAttributeValue("href", "") != url) return null;
        var script = document.SelectSingleNode("//script[@id='__NEXT_DATA__']");
        if (script is null) return null;
        using var data = JsonDocument.Parse(script.InnerText);
        if (data.RootElement.ValueKind != JsonValueKind.Object || !data.RootElement.TryGetProperty("props", out var props) || props.ValueKind != JsonValueKind.Object || !props.TryGetProperty("pageProps", out var page) || page.ValueKind != JsonValueKind.Object || !page.TryGetProperty("initialApolloState", out var state) || state.ValueKind != JsonValueKind.Object) return null;
        var slug = new Uri(url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Last();
        var products = state.EnumerateObject().Where(item => item.Value.ValueKind == JsonValueKind.Object && Text(item.Value, "urlKey") == slug).Select(item => item.Value).ToList();
        if (products.Count != 1) return null;
        var product = products[0];
        if (Text(product, "name") is not { } title || !MetadataEditionMatching.SameTitle(title, edition.Name)) return null;
        var platform = product.TryGetProperty("platform", out var platformData) ? Text(platformData, "label") : null;
        var identity = new GameMetadataIdentityDto { Source = "nintendo", Name = edition.Name, Url = url,
            ReleaseYear = MetadataEditionMatching.Year(Text(product, "releaseDate")), Platforms = platform is null ? [] : [platform] };
        if (MetadataEditionMatching.Conflicts(identity, edition) || edition.ReleaseYear.HasValue && !identity.ReleaseYear.HasValue) return null;
        var result = new PublicGameMetadata { Identity = identity };
        if (missing.Contains("hero") && product.TryGetProperty("productImage", out var hero) && (Text(hero, "url") ?? NintendoAsset(hero, "f_png,c_limit,w_1600")) is { } heroUrl && await VerifyAsync(heroUrl, "hero", lookup, token, refresh)) result.Fields["hero"] = heroUrl;
        if (missing.Contains("logo"))
        {
            var logos = document.SelectNodes("//img[@src and @alt]")?.Where(node =>
                HtmlEntity.DeEntitize(node.GetAttributeValue("alt", "")).Contains(edition.Name, StringComparison.OrdinalIgnoreCase)
                && HtmlEntity.DeEntitize(node.GetAttributeValue("alt", "")).Contains("logo", StringComparison.OrdinalIgnoreCase)).Take(2) ?? [];
            foreach (var logo in logos)
            {
                var logoUrl = HtmlEntity.DeEntitize(logo.GetAttributeValue("src", ""));
                if (!PublicArtworkUrl.Valid(logoUrl) || !await VerifyAsync(logoUrl, "logo", lookup, token, refresh)) continue;
                result.Fields["logo"] = logoUrl; break;
            }
        }
        if (missing.Contains("cover") && product.TryGetProperty("variations", out var variants) && variants.ValueKind == JsonValueKind.Array)
            foreach (var variant in variants.EnumerateArray().Where(item => Text(item, "label")?.StartsWith("Physical Edition", StringComparison.Ordinal) == true).Take(2))
            {
                if (!variant.TryGetProperty("product", out var reference) || Text(reference, "__ref") is not { } key || !state.TryGetProperty(key, out var physical)
                    || Text(physical, "name") is not { } physicalName || !MetadataEditionMatching.SameTitle(physicalName, edition.Name)
                    || !physical.TryGetProperty("productImage", out var image) || NintendoAsset(image, "e_trim/f_png,c_limit,w_600") is not { } coverUrl || !await VerifyAsync(coverUrl, "cover", lookup, token, refresh)) continue;
                result.Fields["cover"] = coverUrl; break;
            }
        return result;
    }
}
