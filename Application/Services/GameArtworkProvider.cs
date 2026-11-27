using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SkiaSharp;

namespace GamesDatabase.Api.Application.Services;

/// <summary>Verified, role-specific public artwork. No credentials in URLs or browser responses.</summary>
public sealed partial class GameArtworkProvider(IHttpClientFactory clients, IMemoryCache cache, TimeProvider clock, IOptions<GameArtworkSettings> settings) : IGameArtworkProvider
{
    private readonly HttpClient client = clients.CreateClient("game-artwork");
    private static readonly string[] Roles = ["logo", "hero", "cover"];
    private static string? Text(JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;

    private async Task<byte[]?> ReadAsync(string url, int limit, string kind, CancellationToken token, string? apiKey = null)
    {
        var uri = new Uri(url);
        var allowed = kind == "image" ? PublicArtworkUrl.Valid(url)
            : uri.Scheme == "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
                && (uri.Host == "www.steamgriddb.com" && uri.AbsolutePath.StartsWith("/api/v2/", StringComparison.Ordinal)
                    || uri.Host == "gamesdb.launchbox-app.com" && uri.AbsolutePath.StartsWith("/games/", StringComparison.Ordinal)
                    || uri.Host == "commons.wikimedia.org" && uri.AbsolutePath == "/w/api.php"
                    || NintendoPage(url));
        if (!allowed || apiKey is not null && uri.Host != "www.steamgriddb.com") throw new InvalidOperationException("Untrusted artwork destination.");
        var cooldown = "artwork-cooldown:" + uri.Host;
        if (cache.TryGetValue<DateTimeOffset>(cooldown, out var until) && until > clock.GetUtcNow())
            throw new MetadataSourceUnavailableException(uri.Host, (int)Math.Ceiling((until - clock.GetUtcNow()).TotalSeconds));
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.ParseAdd(kind == "json" ? "application/json" : kind == "html" ? "text/html" : "image/png,image/jpeg,image/webp");
        if (apiKey is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if ((int)response.StatusCode is 403 or 429 or >= 500)
        {
            var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - clock.GetUtcNow() : TimeSpan.FromMinutes(1));
            delay = TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 3600));
            cache.Set(cooldown, clock.GetUtcNow() + delay, delay);
            throw new MetadataSourceUnavailableException(uri.Host, (int)Math.Ceiling(delay.TotalSeconds));
        }
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        var type = response.Content.Headers.ContentType?.MediaType;
        if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri is { } actual && actual != uri
            || response.Content.Headers.ContentLength > limit || (kind switch { "json" => type != "application/json", "html" => type is not ("text/html" or "application/xhtml+xml"), _ => type is not ("image/png" or "image/jpeg" or "image/webp") }))
            throw new MetadataSourceUnavailableException(uri.Host);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        using var buffer = new MemoryStream(); var block = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(block, token)) > 0)
        {
            if (buffer.Length + count > limit) throw new MetadataSourceUnavailableException(uri.Host);
            buffer.Write(block, 0, count);
        }
        return buffer.ToArray();
    }

    private async Task<string?> HtmlAsync(string url, CancellationToken token, bool refresh)
    {
        var key = "artwork-html-v1:" + url;
        if (!refresh && cache.TryGetValue<string>(key, out var cached)) return cached;
        var bytes = await ReadAsync(url, 1_500_000, "html", token);
        if (bytes is null) return null;
        var html = Encoding.UTF8.GetString(bytes);
        cache.Set(key, html, TimeSpan.FromHours(6));
        return html;
    }

    private async Task<JsonDocument?> JsonAsync(string url, CancellationToken token, string? key = null)
    {
        var bytes = await ReadAsync(url, 1_000_000, "json", token, key);
        if (bytes is null) return null;
        var data = JsonDocument.Parse(bytes);
        if (data.RootElement.ValueKind == JsonValueKind.Object) return data;
        data.Dispose(); throw new JsonException("Invalid artwork source response.");
    }

    private async Task<bool> ImageAsync(string url, string role, CancellationToken token, bool refresh)
    {
        if (!PublicArtworkUrl.Valid(url)) return false;
        var key = "artwork-verified-v1:" + role + ":" + url;
        if (!refresh && cache.TryGetValue<bool>(key, out var valid)) return valid;
        var bytes = await ReadAsync(url, 8_000_000, "image", token);
        valid = false;
        if (bytes is not null && VerifiedImageContent.TryReadRaster(bytes, out var image, maximumPixels: 20_000_000) && image!.MediaType is "image/png" or "image/jpeg" or "image/webp")
        {
            using var data = SKData.CreateCopy(bytes); using var codec = SKCodec.Create(data);
            var info = codec.Info; var ratio = (double)info.Width / info.Height;
            valid = role switch { "hero" => info.Width >= 600 && ratio >= 1.4, "cover" => info.Width >= 200 && ratio is >= 0.45 and <= 0.95, "logo" => info.Width >= 200 && info.Height >= 60, _ => false };
        }
        token.ThrowIfCancellationRequested();
        cache.Set(key, valid, valid ? TimeSpan.FromHours(24) : TimeSpan.FromMinutes(5));
        return valid;
    }

    private static void Failed(GameArtworkLookup result, Exception error)
    {
        result.Unavailable = true;
        if (error is MetadataSourceUnavailableException { RetryAfterSeconds: { } delay }) result.RetryAfterSeconds = Math.Max(result.RetryAfterSeconds ?? 0, delay);
    }

    private async Task<bool> VerifyAsync(string url, string role, GameArtworkLookup result, CancellationToken token, bool refresh)
    {
        try { return await ImageAsync(url, role, token, refresh); }
        catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !token.IsCancellationRequested) { Failed(result, error); return false; }
    }

    public async Task<GameArtworkLookup> LookupAsync(IReadOnlyList<PublicGameMetadata> editionSources, IReadOnlyCollection<string> fields, CancellationToken cancellationToken, bool forceRefresh = false, int? selectedLaunchBoxId = null)
    {
        var result = new GameArtworkLookup();
        var missing = fields.Where(Roles.Contains).ToHashSet(StringComparer.Ordinal);
        if (editionSources.Count == 0 || missing.Count == 0) return result;
        var identity = editionSources[0].Identity;
        var edition = new GameMetadataIdentityDto { Name = identity.Name, SteamAppId = identity.SteamAppId,
            ReleaseYear = editionSources.Select(source => source.Identity.ReleaseYear).FirstOrDefault(year => year.HasValue),
            Platforms = editionSources.Select(source => source.Identity.Platforms).FirstOrDefault(platforms => platforms.Count > 0) ?? [] };
        async Task Attempt(Func<Task<PublicGameMetadata?>> lookup)
        {
            if (missing.Count == 0) return;
            try
            {
                var source = await lookup();
                if (source is null) return;
                foreach (var role in source.Fields.Keys.Where(missing.Contains).ToList()) missing.Remove(role);
                if (source.Fields.Count > 0) result.Sources.Add(source);
            }
            catch (Exception error) when (error is HttpRequestException or JsonException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                Failed(result, error);
            }
        }
        // A selected public LaunchBox edition is an explicit choice, not a request
        // to prefer a similarly named SteamGridDB result over that choice.
        if (selectedLaunchBoxId is null) await Attempt(() => SteamGridDbAsync(edition, missing, result, cancellationToken, forceRefresh));
        await Attempt(() => LaunchBoxAsync(edition, missing, result, selectedLaunchBoxId, cancellationToken, forceRefresh));
        await Attempt(() => NintendoAsync(editionSources, edition, missing, result, cancellationToken, forceRefresh));
        foreach (var source in editionSources)
        {
            if (source.CommonsLogo is { } file && missing.Contains("logo"))
                await Attempt(() => CommonsAsync(source, file, cancellationToken, forceRefresh));
            if (source.ArtworkCandidates.Count == 0) continue;
            await Attempt(async () =>
            {
                var verified = new PublicGameMetadata { Identity = source.Identity };
                foreach (var (role, url) in source.ArtworkCandidates.Where(pair => missing.Contains(pair.Key)))
                    if (await VerifyAsync(url, role, result, cancellationToken, forceRefresh)) verified.Fields[role] = url;
                return verified;
            });
        }
        result.Unavailable &= missing.Count > 0;
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private async Task<PublicGameMetadata?> CommonsAsync(PublicGameMetadata source, string file, CancellationToken token, bool refresh)
    {
        using var data = await JsonAsync("https://commons.wikimedia.org/w/api.php?action=query&format=json&prop=imageinfo&iiprop=url&iiurlwidth=1000&titles=" + Uri.EscapeDataString("File:" + file), token);
        if (data is null || !data.RootElement.TryGetProperty("query", out var query) || query.ValueKind != JsonValueKind.Object || !query.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Object) return null;
        foreach (var page in pages.EnumerateObject())
        {
            if (page.Value.ValueKind != JsonValueKind.Object || !page.Value.TryGetProperty("imageinfo", out var infos) || infos.ValueKind != JsonValueKind.Array) continue;
            foreach (var info in infos.EnumerateArray().Take(1))
            {
                var url = Text(info, "thumburl") ?? Text(info, "url");
                if (url is not null && await ImageAsync(url, "logo", token, refresh))
                    return new() { Identity = source.Identity, Fields = new() { ["logo"] = url } };
            }
        }
        return null;
    }
}
