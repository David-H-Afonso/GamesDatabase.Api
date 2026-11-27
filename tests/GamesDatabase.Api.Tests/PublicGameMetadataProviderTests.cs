using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Common;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace GamesDatabase.Api.Tests;

/// <summary>Public HTTP parsing, provenance, bounds and cancellation without real providers or user data.</summary>
public sealed class PublicGameMetadataProviderTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class Clients(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { Assert.Equal("game-metadata", name); return client; }
    }
    private sealed class Store : ISteamStoreService
    {
        public SteamAppDetailsDto? Details { get; set; }
        public int Reads { get; private set; }
        public Task<SteamAppDetailsDto?> GetAppDetailsAsync(int appId, CancellationToken token = default) { Reads++; token.ThrowIfCancellationRequested(); return Task.FromResult(Details); }
        public Task<SteamAppDetailsDto?> GetOrCacheAppDetailsAsync(int appId) => throw new InvalidOperationException("Metadata previews must not populate persistent Steam cache.");
        public Task<SteamReviewSummaryDto?> GetReviewSummaryAsync(int appId) => throw new InvalidOperationException("Player reviews are not game duration estimates.");
        public Task<List<SteamStoreSearchItemDto>> SearchStoreAsync(string query) => throw new InvalidOperationException("Use the bounded public metadata search.");
        public Task<string?> GetCommunityIconUrlAsync(int appId, CancellationToken token = default) => throw new InvalidOperationException("Artwork owns image resolution.");
        public Task<SteamAssetUrlsDto?> GetAssetUrlsAsync(int appId, CancellationToken token = default) => throw new InvalidOperationException("Artwork owns image resolution.");
        public Task<string?> GetLibraryCoverUrlAsync(int appId) => throw new InvalidOperationException("Artwork owns image resolution.");
    }
    private sealed class Artwork : ISteamArtworkService
    {
        public int Reads { get; private set; }
        public Func<CancellationToken, Task<SteamAssetUrlsDto?>> Resolve { get; set; } = _ => Task.FromResult<SteamAssetUrlsDto?>(null);
        public Task<SteamAssetUrlsDto?> GetAsync(int appId, CancellationToken token = default) { Reads++; return Resolve(token); }
    }
    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    private static PublicGameMetadataProvider Provider(HttpClient http, MemoryCache cache, Store? store = null, Artwork? artwork = null, Clock? clock = null) => new(new Clients(http), store ?? new(), artwork ?? new(), cache, clock ?? new());
    private static HttpClient NoPublicReads() => new(new Handler((_, _) => throw new InvalidOperationException("This test must not perform a public HTTP lookup.")));
    private static object Claim(object value) => new { rank = "normal", mainsnak = new { datavalue = new { value } } };
    private static object Entity(string name, Dictionary<string, object[]> claims) => new { labels = new { en = new { value = name } }, claims };
    private static object Item(string id) => new { id };

    [Fact]
    public async Task Steam_returns_verified_facts_and_manual_zero_critic_but_never_estimates_or_persistent_cache()
    {
        using var http = NoPublicReads(); using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new Store { Details = new() { Name = "Verified title", Developer = "  Verified studio  ", ReleaseDate = "Feb 29, 2024", MetacriticScore = 0 } }; var artwork = new Artwork();
        var result = await Provider(http, cache, store, artwork).SteamAsync(100, false, default);
        Assert.Equal("steam", result!.Identity.Source); Assert.Equal("https://store.steampowered.com/app/100/", result.Identity.Url);
        Assert.Equal("Verified studio", result.Fields["developer"]); Assert.Equal("2024-02-29", result.Fields["released"]); Assert.Equal(0, result.Fields["critic"]);
        Assert.Equal(new[] { "critic", "developer", "name", "released" }, result.Fields.Keys.Order()); Assert.Equal(1, store.Reads); Assert.Equal(0, artwork.Reads);
    }

    [Fact]
    public async Task Missing_or_invalid_facts_remain_absent_and_only_trusted_https_artwork_is_exposed()
    {
        using var http = NoPublicReads(); using var cache = new MemoryCache(new MemoryCacheOptions());
        var store = new Store { Details = new() { Name = "Verified title", Developer = new string('x', 151), ReleaseDate = "Coming soon", MetacriticScore = 101 } };
        var artwork = new Artwork { Resolve = _ => Task.FromResult<SteamAssetUrlsDto?>(new() { LogoUrl = "https://shared.akamai.steamstatic.com/apps/100/content-hash/logo.png", HeroUrl = "javascript:alert(1)", CoverUrl = "https://user:password@shared.akamai.steamstatic.com/private.jpg" }) };
        var result = await Provider(http, cache, store, artwork).SteamAsync(100, true, default);
        Assert.Equal(new[] { "logo", "name" }, result!.Fields.Keys.Order()); Assert.Contains("content-hash", (string)result.Fields["logo"]);
        store.Details = null; Assert.Null(await Provider(http, cache, store, artwork).SteamAsync(100, true, default)); Assert.Equal(1, artwork.Reads);
    }

    [Theory]
    [InlineData("http")]
    [InlineData("json")]
    [InlineData("timeout")]
    public async Task Optional_artwork_failure_does_not_discard_verified_store_facts(string failure)
    {
        using var http = NoPublicReads(); using var cache = new MemoryCache(new MemoryCacheOptions());
        var artwork = new Artwork { Resolve = _ => throw failure switch { "http" => new HttpRequestException("Fixture unavailable"), "json" => new JsonException("Fixture malformed"), _ => new OperationCanceledException("Fixture provider timeout") } };
        var result = await Provider(http, cache, new Store { Details = new() { Name = "Verified title", Developer = "Verified studio" } }, artwork).SteamAsync(100, true, default);
        Assert.Equal("Verified studio", result!.Fields["developer"]); Assert.DoesNotContain("hero", result.Fields.Keys);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_treated_as_optional_artwork_failure()
    {
        using var http = NoPublicReads(); using var cache = new MemoryCache(new MemoryCacheOptions()); using var cancellation = new CancellationTokenSource();
        var artwork = new Artwork { Resolve = token => { cancellation.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult<SteamAssetUrlsDto?>(null); } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider(http, cache, new Store { Details = new() { Name = "Verified title" } }, artwork).SteamAsync(100, true, cancellation.Token));
    }

    [Fact]
    public async Task Steam_search_encodes_literal_names_and_rejects_non_numeric_or_invalid_ids()
    {
        const string name = "O'Brien & appids=999";
        using var http = new HttpClient(new Handler((request, _) => {
            Assert.Equal("store.steampowered.com", request.RequestUri!.Host); Assert.Contains("term=" + Uri.EscapeDataString(name), request.RequestUri.OriginalString);
            return Task.FromResult(Json(new { items = new object[] { new { id = 100, name }, new { id = -1, name = "Negative" }, new { id = "sql", name = "Invalid" }, new { id = 0, name = "Zero" } } }));
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = Assert.Single(await Provider(http, cache).SearchSteamAsync(name, default)); Assert.Equal(100, result.SteamAppId); Assert.Equal(name, result.Name);
    }

    [Fact]
    public async Task Wikidata_uses_only_validated_entity_ids_and_keeps_ambiguous_or_low_precision_dates_absent()
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((request, _) => {
            calls++;
            Assert.Equal("www.wikidata.org", request.RequestUri!.Host);
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            if (query["action"] == "wbsearchentities") return Task.FromResult(Json(new { search = new[] { new { id = "Q1", label = "Verified title" }, new { id = "Q2", label = "Q2" }, new { id = "Q3", label = "Another title" }, new { id = "Q1 } SERVICE <https://invalid.test> {", label = "Unsafe" } } }));
            if (query["props"] == "labels") return Task.FromResult(Json(new { entities = new Dictionary<string, object> { ["Q10"] = Entity("Studio A", []), ["Q11"] = Entity("Studio B", []) } }));
            Assert.Equal("Q1|Q2|Q3", query["ids"].ToString()); Assert.DoesNotContain("invalid.test", query["ids"].ToString());
            return Task.FromResult(Json(new { entities = new Dictionary<string, object> {
                ["Q1"] = Entity("Verified title", new() { ["P31"] = [Claim(Item("Q7889"))], ["P178"] = [Claim(Item("Q11")), Claim(Item("Q10"))], ["P577"] = [Claim(new { time = "+2024-02-29T00:00:00Z", precision = 11 }), Claim(new { time = "+2024-03-01T00:00:00Z", precision = 11 })] }),
                ["Q2"] = Entity("Q2", new() { ["P31"] = [Claim(Item("Q7889"))], ["P577"] = [Claim(new { time = "+2024-01-01T00:00:00Z", precision = 9 })] }),
                ["Q3"] = Entity("Another title", new() { ["P31"] = [Claim(Item("Q7889"))], ["P577"] = [Claim(new { time = "+2024-02-29T00:00:00Z", precision = 11 }), Claim(new { time = "+2024-01-01T00:00:00Z", precision = 9 })] }) } }));
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var results = await Provider(http, cache).WikidataAsync("Verified title", null, default);
        Assert.Equal(3, calls); Assert.Equal(2, results.Count); Assert.Equal("Studio A, Studio B", results[0].Fields["developer"]); Assert.DoesNotContain("released", results[0].Fields.Keys);
        Assert.Equal("2024-02-29", results[1].Fields["released"]); Assert.DoesNotContain("developer", results[1].Fields.Keys); Assert.All(results, result => Assert.Equal("wikidata", result.Identity.Source));
    }

    [Fact]
    public async Task Retry_after_respects_the_provider_host_and_expires_at_the_injected_clock()
    {
        var steamReads = 0; var wikiReads = 0; var clock = new Clock();
        using var http = new HttpClient(new Handler((request, _) => {
            if (request.RequestUri!.Host == "store.steampowered.com") { steamReads++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)429) { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2)) } }); }
            wikiReads++; return Task.FromResult(Json(new { search = Array.Empty<object>() }));
        })); using var cache = new MemoryCache(new MemoryCacheOptions()); var provider = Provider(http, cache, clock: clock);
        Assert.Equal(120, (await Assert.ThrowsAsync<MetadataSourceUnavailableException>(() => provider.SearchSteamAsync("First", default))).RetryAfterSeconds);
        await Assert.ThrowsAsync<MetadataSourceUnavailableException>(() => provider.SearchSteamAsync("Second", default)); Assert.Equal(1, steamReads);
        Assert.Empty(await provider.WikidataAsync("Another host", null, default)); Assert.Equal(1, wikiReads);
        clock.Now += TimeSpan.FromMinutes(2); await Assert.ThrowsAsync<MetadataSourceUnavailableException>(() => provider.SearchSteamAsync("After cooldown", default)); Assert.Equal(2, steamReads);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Oversized_declared_or_streamed_JSON_is_rejected_before_parsing(bool declared)
    {
        using var http = new HttpClient(new Handler((_, _) => {
            var content = new StringContent(new string('x', 1_000_001)); if (!declared) content.Headers.ContentLength = null;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        await Assert.ThrowsAsync<MetadataSourceUnavailableException>(() => Provider(http, cache).SearchSteamAsync("Oversized fixture", default));
    }

    [Fact]
    public async Task Punctuation_sensitive_search_uses_bounded_keyword_discovery_but_returns_the_full_canonical_identity()
    {
        var searches = new List<string>();
        using var http = new HttpClient(new Handler((request, _) => {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            if (query["action"] == "wbsearchentities")
            {
                searches.Add(query["search"].ToString());
                return Task.FromResult(Json(new { search = searches.Count == 1 ? Array.Empty<object>() : [new { id = "Q133567959", label = "Tomodachi Life: Living the Dream" }] }));
            }
            return Task.FromResult(Json(new { entities = new Dictionary<string, object> { ["Q133567959"] = Entity("Tomodachi Life: Living the Dream", new() { ["P31"] = [Claim(Item("Q7889"))], ["P577"] = [Claim(new { time = "+2026-04-16T00:00:00Z", precision = 11 })] }) } }));
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = Assert.Single(await Provider(http, cache).WikidataAsync("tomodachi life living the dream", null, default));
        Assert.Equal(new[] { "tomodachi life living the dream", "tomodachi life" }, searches);
        Assert.Equal("Tomodachi Life: Living the Dream", result.Identity.Name); Assert.Equal(2026, result.Identity.ReleaseYear); Assert.Equal("2026-04-16", result.Fields["released"]);
    }

    [Fact]
    public async Task Explicit_Wikidata_id_does_not_depend_on_search_ranking_or_the_failing_SPARQL_endpoint()
    {
        using var http = new HttpClient(new Handler((request, _) => {
            Assert.Equal("www.wikidata.org", request.RequestUri!.Host);
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query); Assert.Equal("wbgetentities", query["action"].ToString()); Assert.Equal("Q140140365", query["ids"].ToString());
            return Task.FromResult(Json(new { entities = new Dictionary<string, object> { ["Q140140365"] = new { labels = new { mul = new { value = "The Legend of Zelda: Ocarina of Time" } }, claims = new Dictionary<string, object[]> { ["P31"] = [Claim(Item("Q7889"))], ["P577"] = [Claim(new { time = "+2026-11-05T00:00:00Z", precision = 11 })] } } } }));
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = Assert.Single(await Provider(http, cache).WikidataAsync("A custom library title", null, default, "Q140140365"));
        Assert.Equal(2026, result.Identity.ReleaseYear); Assert.Equal("2026-11-05", result.Fields["released"]);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(502)]
    public async Task Provider_failure_is_never_an_empty_search_or_permission_for_a_keyword_retry(int status)
    {
        var calls = 0;
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)); })); using var cache = new MemoryCache(new MemoryCacheOptions());
        await Assert.ThrowsAsync<MetadataSourceUnavailableException>(() => Provider(http, cache).WikidataAsync("A full game name", null, default)); Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_published_full_brand_alias_resolves_Wolverine_without_matching_the_unrelated_podcast()
    {
        using var http = new HttpClient(new Handler((request, _) => {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            if (query["action"] == "wbsearchentities") return Task.FromResult(Json(new { search = new[] { new { id = "Q108479474", label = "Wolverine" }, new { id = "Q48742103", label = "Wolverine: The Long Night" } } }));
            return Task.FromResult(Json(new { entities = new Dictionary<string, object> {
                ["Q108479474"] = new { labels = new { en = new { value = "Wolverine" } }, aliases = new { mul = new[] { new { value = "Marvel's Wolverine" }, new { value = "Marvels Wolverine" } } }, claims = new Dictionary<string, object[]> { ["P31"] = [Claim(Item("Q7889"))], ["P577"] = [Claim(new { time = "+2026-09-15T00:00:00Z", precision = 11 })] } },
                ["Q48742103"] = Entity("Wolverine: The Long Night", new() { ["P31"] = [Claim(Item("Q24634210"))] }) } }));
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = Assert.Single(await Provider(http, cache).WikidataAsync("Marvel’s Wolverine", null, default));
        Assert.Equal("Marvel's Wolverine", result.Identity.Name); Assert.Equal("Q108479474", result.Identity.WikidataId); Assert.Equal(2026, result.Identity.ReleaseYear);
    }

    [Fact]
    public async Task Direct_entity_JSON_keeps_bounded_video_game_subclass_support_without_SPARQL()
    {
        using var http = new HttpClient(new Handler((request, _) => {
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query); Assert.Equal("www.wikidata.org", request.RequestUri.Host);
            if (query["action"] == "wbsearchentities") return Task.FromResult(Json(new { search = new[] { new { id = "Q12345", label = "Verified subtype game" } } }));
            if (query["props"] == "claims") { Assert.Equal("Q90000", query["ids"].ToString()); return Task.FromResult(Json(new { entities = new Dictionary<string, object> { ["Q90000"] = Entity("Fixture video game subtype", new() { ["P279"] = [Claim(Item("Q7889"))] }) } })); }
            return Task.FromResult(Json(new { entities = new Dictionary<string, object> { ["Q12345"] = Entity("Verified subtype game", new() { ["P31"] = [Claim(Item("Q90000"))] }) } }));
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.Equal("Q12345", Assert.Single(await Provider(http, cache).WikidataAsync("Verified subtype game", null, default)).Identity.WikidataId);
    }

    [Fact]
    public async Task Cancellation_reaches_the_public_HTTP_request()
    {
        using var cancellation = new CancellationTokenSource(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return Json(new { }); })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var request = Provider(http, cache).SearchSteamAsync("Cancellable fixture", cancellation.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }
}
