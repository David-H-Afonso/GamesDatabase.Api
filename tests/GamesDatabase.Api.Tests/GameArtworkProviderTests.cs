using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Contracts;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace GamesDatabase.Api.Tests;

/// <summary>Concrete public HTML parsing and bounded native image decoding, not live-source proof.</summary>
public sealed class GameArtworkProviderTests
{
    private const string Logo = "https://images.launchbox-app.com/11111111-1111-4111-8111-111111111111.png";
    private const string Hero = "https://images.launchbox-app.com/22222222-2222-4222-8222-222222222222.jpg";
    private const string Cover = "https://gamesdb-images.launchbox.gg/r2_33333333-3333-4333-8333-333333333333.png";
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class Clients(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { Assert.Equal("game-artwork", name); return client; }
    }
    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") };
    private static HttpResponseMessage Image(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height); bitmap.Erase(SKColors.CadetBlue); using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return new(HttpStatusCode.OK) { Content = new ByteArrayContent(data.ToArray()) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } } };
    }
    private static GameArtworkProvider Provider(HttpClient client, MemoryCache cache, string? key = null) => new(new Clients(client), cache, TimeProvider.System, Options.Create(new GameArtworkSettings { SteamGridDbApiKey = key }));
    private static PublicGameMetadata Edition(string name, int? year = 2026, params string[] platforms) => new() { Identity = new() { Source = "wikidata", Name = name, WikidataId = "Q12345", Url = "https://www.wikidata.org/wiki/Q12345", ReleaseYear = year, Platforms = platforms.ToList() } };
    private static string Card(string name, int id, string platform, int year = 2026) => $"<a class='list-item link-no-underline' href='/games/details/{id}-verified-game'><div class='cardTitle'><h3>{name}</h3><p>{platform}</p></div><div class='releaseDate'><h5>November 5, {year}</h5></div></a>";
    private static string Search(params string[] cards) => "<div id='cardsContainer'>" + string.Join("", cards) + "</div>";
    private static string Page(string name, string platform, int year = 2026, string? cover = null) => $"<h1>{name}</h1><dl><dt>Release Date</dt><dd>November 5, {year}</dd><dt>Platform</dt><dd>{platform}</dd></dl><img src='{Logo}' alt='{name} - Clear Logo (World) - 600x180'><img src='{Hero}' alt='{name} - Fanart - Background (World) - 1280x720'><img src='{cover ?? Cover}' alt='{name} - Box - Front (North America) - 600x900'>";
    private static HttpResponseMessage Artwork(HttpRequestMessage request) => request.RequestUri!.AbsoluteUri == Logo ? Image(600, 180) : request.RequestUri.AbsoluteUri == Hero ? Image(1280, 720) : Image(600, 900);

    [Theory]
    [InlineData("Tomodachi Life: Living the Dream", "Nintendo Switch", 2026)]
    [InlineData("Marvel’s Wolverine", "Sony Playstation 5", 2026)]
    [InlineData("Grand Theft Auto VI", "Sony Playstation 5", 2026)]
    [InlineData("The Legend of Zelda: Ocarina of Time", "Nintendo 64", 1998)]
    public async Task Keyless_fallback_supplies_three_distinct_verified_roles_without_a_Steam_release(string name, string platform, int year)
    {
        var requests = new List<Uri>();
        using var http = new HttpClient(new Handler((request, _) => {
            Assert.Equal(HttpMethod.Get, request.Method); Assert.Null(request.Headers.Authorization); Assert.False(request.Headers.Contains("Cookie")); requests.Add(request.RequestUri!);
            if (request.RequestUri!.AbsolutePath.StartsWith("/games/results/", StringComparison.Ordinal)) Assert.Equal(Uri.UnescapeDataString(request.RequestUri.AbsolutePath).ToLowerInvariant(), Uri.UnescapeDataString(request.RequestUri.AbsolutePath));
            return Task.FromResult(request.RequestUri!.Host == "gamesdb.launchbox-app.com" ? Html(request.RequestUri.AbsolutePath.StartsWith("/games/results/", StringComparison.Ordinal) ? Search(Card(name, 10, platform, year)) : Page(name, platform, year)) : Artwork(request));
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var lookup = await Provider(http, cache).LookupAsync([Edition(name, year, platform)], ["logo", "hero", "cover"], default);
        var result = Assert.Single(lookup.Sources); Assert.Equal("launchbox", result.Identity.Source); Assert.Equal(new[] { "cover", "hero", "logo" }, result.Fields.Keys.Order()); Assert.Equal(3, result.Fields.Values.Distinct().Count()); Assert.False(lookup.Unavailable);
        Assert.DoesNotContain(requests, uri => uri.Host.Contains("steam", StringComparison.Ordinal)); Assert.Equal(5, requests.Count);
    }

    [Fact]
    public async Task Same_title_different_year_or_platform_never_supplies_the_other_editions_images()
    {
        var paths = new List<string>(); const string name = "The Legend of Zelda: Ocarina of Time";
        using var http = new HttpClient(new Handler((request, _) => {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.RequestUri.Host != "gamesdb.launchbox-app.com" ? Artwork(request) : Html(request.RequestUri.AbsolutePath.StartsWith("/games/results/", StringComparison.Ordinal) ? Search(Card(name, 1, "Nintendo 64", 1998), Card(name, 2, "Nintendo Switch 2"), Card(name + " (Remake)", 3, "Windows")) : Page(name, "Nintendo Switch 2")));
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await Provider(http, cache).LookupAsync([Edition(name, 2026, "Switch 2")], ["cover"], default);
        Assert.Equal(2, Assert.Single(result.Sources).Identity.LaunchBoxId); Assert.Single(result.Matches); Assert.DoesNotContain("/games/details/1-verified-game", paths); Assert.DoesNotContain("/games/details/3-verified-game", paths);
    }

    [Fact]
    public async Task Ambiguous_platforms_require_an_explicit_in_search_choice_and_arbitrary_ids_are_not_requested()
    {
        const string name = "Grand Theft Auto VI"; var paths = new List<string>();
        using var http = new HttpClient(new Handler((request, _) => {
            paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(request.RequestUri.Host != "gamesdb.launchbox-app.com" ? Artwork(request) : Html(request.RequestUri.AbsolutePath.StartsWith("/games/results/", StringComparison.Ordinal) ? Search(Card(name, 1, "Microsoft Xbox Series X/S"), Card(name, 2, "Sony Playstation 5")) : Page(name, "Sony Playstation 5")));
        })); using var cache = new MemoryCache(new MemoryCacheOptions()); var provider = Provider(http, cache); var evidence = new[] { Edition(name, 2026, "PS5", "Xbox Series X/S") };
        var ambiguous = await provider.LookupAsync(evidence, ["cover"], default); Assert.Empty(ambiguous.Sources); Assert.Equal(2, ambiguous.Matches.Count); Assert.Single(paths);
        Assert.Empty((await provider.LookupAsync(evidence, ["cover"], default, selectedLaunchBoxId: 999)).Sources); Assert.Single(paths);
        Assert.Equal(2, Assert.Single((await provider.LookupAsync(evidence, ["cover"], default, selectedLaunchBoxId: 2)).Sources).Identity.LaunchBoxId);
    }

    [Fact]
    public async Task Original_image_mapping_is_bound_to_the_selected_labelled_thumbnail_not_related_page_state()
    {
        const string name = "Verified game"; const string thumbnail = "https://gamesdb-images.launchbox.gg/r2_44444444-4444-4444-8444-444444444444.png"; var urls = new List<string>();
        var state = JsonSerializer.Serialize(new object[] { new { imageFileName = 1, fullGameImageFileName = 2 }, "r2_44444444-4444-4444-8444-444444444444.png", "r2_33333333-3333-4333-8333-333333333333.png", new { imageFileName = 4, fullGameImageFileName = 5 }, "unrelated.png", "r2_55555555-5555-4555-8555-555555555555.png" });
        using var http = new HttpClient(new Handler((request, _) => { urls.Add(request.RequestUri!.AbsoluteUri); return Task.FromResult(request.RequestUri.Host != "gamesdb.launchbox-app.com" ? Artwork(request) : Html(request.RequestUri.AbsolutePath.StartsWith("/games/results/", StringComparison.Ordinal) ? Search(Card(name, 10, "PS5")) : Page(name, "PS5", cover: thumbnail) + $"<script id='__NUXT_DATA__' type='application/json'>{state}</script>")); })); using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.Equal(Cover, Assert.Single((await Provider(http, cache).LookupAsync([Edition(name, 2026, "PS5")], ["cover"], default)).Sources).Fields["cover"]);
        Assert.DoesNotContain(thumbnail, urls); Assert.DoesNotContain(urls, url => url.Contains("55555555", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("html")]
    [InlineData("truncated")]
    [InlineData("landscape-cover")]
    [InlineData("untrusted-host")]
    public async Task Invalid_or_wrong_role_content_is_not_a_verified_image(string failure)
    {
        var cover = failure == "untrusted-host" ? "https://127.0.0.1/private.png" : Cover;
        using var http = new HttpClient(new Handler((request, _) => {
            Assert.NotEqual("127.0.0.1", request.RequestUri!.Host);
            if (request.RequestUri.Host == "gamesdb.launchbox-app.com") return Task.FromResult(Html(request.RequestUri.AbsolutePath.StartsWith("/games/results/", StringComparison.Ordinal) ? Search(Card("Verified game", 1, "PS5")) : Page("Verified game", "PS5", cover: cover)));
            if (failure == "landscape-cover") return Task.FromResult(Image(1280, 720));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(failure == "html" ? "<html>not an image</html>"u8.ToArray() : [137, 80, 78, 71, 13, 10, 26, 10]) { Headers = { ContentType = new MediaTypeHeaderValue("image/png") } } });
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.Empty((await Provider(http, cache).LookupAsync([Edition("Verified game", 2026, "PS5")], ["cover"], default)).Sources);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public async Task Force_refresh_never_bypasses_a_public_host_cooldown(int status)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2)) } }); })); using var cache = new MemoryCache(new MemoryCacheOptions()); var provider = Provider(http, cache);
        Assert.True((await provider.LookupAsync([Edition("Verified game")], ["cover"], default)).Unavailable);
        var second = await provider.LookupAsync([Edition("Verified game")], ["cover"], default, forceRefresh: true); Assert.True(second.Unavailable); Assert.InRange(second.RetryAfterSeconds!.Value, 119, 120); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task Optional_SteamGridDB_key_is_sent_only_to_the_documented_API_and_never_to_CDNs_or_results()
    {
        const string key = "SYNTHETIC_KEY_NOT_A_REAL_CREDENTIAL"; const string name = "Verified game";
        var apiReads = 0; var imageReads = 0;
        using var http = new HttpClient(new Handler((request, _) => {
            if (request.RequestUri!.Host == "www.steamgriddb.com")
            {
                apiReads++; Assert.Equal("Bearer", request.Headers.Authorization?.Scheme); Assert.Equal(key, request.Headers.Authorization?.Parameter);
                if (request.RequestUri.AbsolutePath.Contains("/search/", StringComparison.Ordinal)) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = new[] { new { id = 100, name, release_date = new DateTimeOffset(2026, 11, 5, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds() } } }) });
                var resource = request.RequestUri.AbsolutePath.Split('/')[3]; var role = resource == "grids" ? "grid" : resource == "heroes" ? "hero" : "logo";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { success = true, data = new[] { new { url = $"https://cdn2.steamgriddb.com/{role}/1234567890abcdef.png", nsfw = false, humor = false, epilepsy = false } } }) });
            }
            Assert.Equal("cdn2.steamgriddb.com", request.RequestUri.Host); Assert.Null(request.Headers.Authorization); imageReads++;
            return Task.FromResult(request.RequestUri.AbsolutePath.StartsWith("/logo/", StringComparison.Ordinal) ? Image(600, 180) : request.RequestUri.AbsolutePath.StartsWith("/hero/", StringComparison.Ordinal) ? Image(1280, 720) : Image(600, 900));
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await Provider(http, cache, key).LookupAsync([Edition(name)], ["logo", "hero", "cover"], default);
        var source = Assert.Single(result.Sources); Assert.Equal("steamgriddb", source.Identity.Source); Assert.Equal(3, source.Fields.Count); Assert.Equal(4, apiReads); Assert.Equal(3, imageReads); Assert.DoesNotContain(key, JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task An_independent_image_failure_keeps_the_other_verified_roles_and_reports_partial_availability()
    {
        using var http = new HttpClient(new Handler((request, _) => {
            if (request.RequestUri!.Host == "gamesdb.launchbox-app.com") return Task.FromResult(Html(request.RequestUri.AbsolutePath.StartsWith("/games/results/", StringComparison.Ordinal) ? Search(Card("Verified game", 1, "PS5")) : Page("Verified game", "PS5")));
            return Task.FromResult(request.RequestUri.AbsoluteUri == Cover ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Artwork(request));
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await Provider(http, cache).LookupAsync([Edition("Verified game", 2026, "PS5")], ["logo", "hero", "cover"], default);
        Assert.True(result.Unavailable); Assert.Equal(new[] { "hero", "logo" }, Assert.Single(result.Sources).Fields.Keys.Order());
    }

    [Fact]
    public async Task A_Deku_screenshot_on_the_official_PlayStation_CDN_fills_only_the_missing_hero()
    {
        const string sony = "https://image.api.playstation.com/vulcan/ap/rnd/202505/0616/abcdef0123456789.jpg";
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(request.RequestUri!.Host == "gamesdb.launchbox-app.com" ? Html("<div id='cardsContainer'></div>") : Image(1920, 1080)))); using var cache = new MemoryCache(new MemoryCacheOptions());
        var edition = Edition("Grand Theft Auto VI", 2026, "PS5"); edition.Identity.Source = "dekudeals"; edition.Identity.Url = "https://www.dekudeals.com/items/grand-theft-auto-vi"; edition.ArtworkCandidates["hero"] = sony;
        var result = Assert.Single((await Provider(http, cache).LookupAsync([edition], ["hero"], default)).Sources); Assert.Equal(sony, result.Fields["hero"]); Assert.Equal("dekudeals", result.Identity.Source); Assert.Single(result.Fields);
    }

    [Theory]
    [InlineData("https://images.launchbox-app.com.evil.invalid/11111111-1111-4111-8111-111111111111.png")]
    [InlineData("https://fixture:password@images.launchbox-app.com/11111111-1111-4111-8111-111111111111.png")]
    [InlineData("http://images.launchbox-app.com/11111111-1111-4111-8111-111111111111.png")]
    [InlineData("https://images.launchbox-app.com/11111111-1111-4111-8111-111111111111.png?redirect=http://127.0.0.1")]
    [InlineData("https://assets.nintendo.com/image/fetch/https://127.0.0.1/private.png")]
    public void Artwork_URL_policy_rejects_credentials_queries_fetch_proxies_and_lookalike_hosts(string url) => Assert.False(PublicArtworkUrl.Valid(url));

    [Fact]
    public void Existing_Steam_timestamp_cache_busters_remain_valid_without_accepting_arbitrary_queries()
    {
        Assert.True(PublicArtworkUrl.Valid("https://shared.akamai.steamstatic.com/apps/100/logo.png?t=1760000000"));
        Assert.False(PublicArtworkUrl.Valid("https://shared.akamai.steamstatic.com/apps/100/logo.png?redirect=http://127.0.0.1"));
    }
}
