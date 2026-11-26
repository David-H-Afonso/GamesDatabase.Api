using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Contracts.Steam;
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
        object Binding(string id, string title, string developer, string date, string precision) => new { item = new { value = "http://www.wikidata.org/entity/" + id }, itemLabel = new { value = title }, developerLabel = new { value = developer }, date = new { value = date }, precision = new { value = precision } };
        using var http = new HttpClient(new Handler((request, _) => {
            calls++;
            if (request.RequestUri!.Host == "www.wikidata.org") return Task.FromResult(Json(new { search = new[] { new { id = "Q1" }, new { id = "Q2" }, new { id = "Q3" }, new { id = "Q1 } SERVICE <https://invalid.test> {" } } }));
            Assert.Equal("query.wikidata.org", request.RequestUri.Host); var query = Uri.UnescapeDataString(request.RequestUri.Query); Assert.Contains("VALUES ?item { wd:Q1 wd:Q2 wd:Q3 }", query); Assert.DoesNotContain("invalid.test", query); Assert.DoesNotContain("O'Brien", query);
            return Task.FromResult(Json(new { results = new { bindings = new[] { Binding("Q1", "Verified title", "Studio B", "2024-02-29T00:00:00Z", "11"), Binding("Q1", "Verified title", "Studio A", "2024-03-01T00:00:00Z", "11"), Binding("Q2", "Q2", "Q44", "2024-01-01T00:00:00Z", "9"), Binding("Q3", "Another title", "Q44", "2024-02-29T00:00:00Z", "11"), Binding("Q3", "Another title", "Q44", "2024-01-01T00:00:00Z", "9") } } }));
        })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var results = await Provider(http, cache).WikidataAsync("O'Brien { not SPARQL }", null, default);
        Assert.Equal(2, calls); Assert.Equal(2, results.Count); Assert.Equal("Studio A, Studio B", results[0].Fields["developer"]); Assert.DoesNotContain("released", results[0].Fields.Keys);
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
        Assert.Empty(await provider.SearchSteamAsync("First", default)); Assert.Empty(await provider.SearchSteamAsync("Second", default)); Assert.Equal(1, steamReads);
        Assert.Empty(await provider.WikidataAsync("Another host", null, default)); Assert.Equal(1, wikiReads);
        clock.Now += TimeSpan.FromMinutes(2); Assert.Empty(await provider.SearchSteamAsync("After cooldown", default)); Assert.Equal(2, steamReads);
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
        Assert.Empty(await Provider(http, cache).SearchSteamAsync("Oversized fixture", default));
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
