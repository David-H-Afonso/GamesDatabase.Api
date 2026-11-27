using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class DekuDealsDurationTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 26, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class Clients(HttpClient http) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) { Assert.Equal("game-durations", name); return http; }
    }
    private const string Search = "<main><a class='main-link flex-grow-1' href='/items/amber-trail'><h6>Amber Trail</h6></a></main>";
    private const string AllPlatforms = "switch_2,switch,ps5,ps4,xbox_series,xbox_one,steam";
    private static string Page(string story = "23½ hours", string completion = "65½ hours", string slug = "amber-trail", string title = "Amber Trail") => $"""
        <html><head><meta property='og:title' content='{title} | Deku Deals'><link rel='canonical' href='https://www.dekudeals.com/items/{slug}'></head>
        <body><main><ul><li><strong><a href='https://howlongtobeat.com/game.php?id=12345'>How Long To Beat</a>:</strong>
        <ul><li><strong>Main Story:</strong> {story}</li><li><strong>Main + Extra:</strong> 999 hours</li><li><strong>Completionist:</strong> {completion}</li></ul></li></ul></main></body></html>
        """;
    private static HttpResponseMessage Html(string html) => new(HttpStatusCode.OK) { Content = new StringContent(html, System.Text.Encoding.UTF8, "text/html") };
    private static DekuDealsDurationProvider Provider(HttpClient http, MemoryCache cache, Clock? clock = null) => new(new Clients(http), cache, clock ?? new());

    [Theory]
    [InlineData("27 hours", 27, "27")]
    [InlineData("23½ hours", 24, "23.5")]
    [InlineData("2¼ hours", 2, "2.25")]
    [InlineData("2¾ hours", 3, "2.75")]
    [InlineData("0.5 hours", 1, "0.5")]
    [InlineData("15 Minutes", 1, "0.25")]
    public async Task Reads_only_attributed_story_completion_and_keeps_original_precision_for_review(string story, int proposed, string original)
    {
        var urls = new List<string>();
        using var http = new HttpClient(new Handler((request, _) => { urls.Add(request.RequestUri!.ToString()); return Task.FromResult(Html(request.RequestUri.AbsolutePath == "/search" ? Search : Page(story))); })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await Provider(http, cache).LookupAsync("Amber Trail", null, default);
        Assert.NotNull(result.Metadata); Assert.Equal("dekudeals", result.Metadata.Identity.Source); Assert.Equal("https://www.dekudeals.com/items/amber-trail", result.Metadata.Identity.Url);
        Assert.Equal(proposed, result.Metadata.Fields["story"]); Assert.Equal(decimal.Parse(original, CultureInfo.InvariantCulture), result.Metadata.SourceHours["story"]);
        Assert.Equal(66, result.Metadata.Fields["completion"]); Assert.Equal(65.5m, result.Metadata.SourceHours["completion"]); Assert.Equal(2, result.Metadata.Fields.Count);
        Assert.All(urls, url => Assert.StartsWith("https://www.dekudeals.com/", url)); Assert.Equal(2, urls.Count);
    }

    [Fact]
    public async Task Caches_parsed_searches_and_durations_without_repeating_public_requests()
    {
        var reads = 0;
        using var http = new HttpClient(new Handler((request, _) => { reads++; return Task.FromResult(Html(request.RequestUri!.AbsolutePath == "/search" ? Search : Page())); })); using var cache = new MemoryCache(new MemoryCacheOptions()); var provider = Provider(http, cache);
        Assert.NotNull((await provider.LookupAsync("Amber Trail", null, default)).Metadata); Assert.NotNull((await provider.LookupAsync("Amber Trail", null, default)).Metadata); Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData("Grand Theft Auto VI", "grand-theft-auto-vi", "48½ hours", "N/A", 99, 49)]
    [InlineData("CONTROL Resonant", "control-resonant", "24½ hours", "44 hours", 83, 25)]
    public async Task Searches_every_supported_platform_explicitly_instead_of_accepting_the_anonymous_Nintendo_only_default(string name, string slug, string story, string completion, int critic, int roundedStory)
    {
        var reads = new List<Uri>();
        var search = $"<main><a class='main-link' href='/items/{slug}'><h6>{name}</h6></a><a class='main-link' href='/items/{slug}-deluxe'>{name} Deluxe</a></main>";
        var page = Page(story, completion, slug, name).Replace("</main>", $"<li><strong>Metacritic:</strong><a class='metacritic' href='https://www.metacritic.com/game/{slug}/'><span class='text-white'>{critic}</span><span>9.9</span></a></li></main>");
        using var http = new HttpClient(new Handler((request, _) =>
        {
            reads.Add(request.RequestUri!);
            Assert.False(request.Headers.Contains("Cookie"));
            if (request.RequestUri!.AbsolutePath != "/search") return Task.FromResult(Html(page));
            var query = QueryHelpers.ParseQuery(request.RequestUri.Query);
            Assert.Equal(name, query["q"].ToString());
            // These are the observed anonymous defaults: no CONTROL and a
            // different Vice City edition. The explicit supported platform list
            // is what makes the exact public PS/Xbox/Steam record discoverable.
            var allPlatforms = query.TryGetValue("platforms", out var platforms) && platforms.ToString() == AllPlatforms;
            return Task.FromResult(Html(allPlatforms ? search : name == "CONTROL Resonant" ? "<main>No items matched your search</main>" : "<main><a class='main-link' href='/items/grand-theft-auto-vice-city'>Grand Theft Auto: Vice City</a></main>"));
        }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await Provider(http, cache).LookupAsync(name, null, default);
        Assert.False(result.Unavailable); Assert.NotNull(result.Metadata);
        Assert.Equal(slug, result.Metadata.Identity.DekuDealsSlug); Assert.Equal(roundedStory, result.Metadata.Fields["story"]);
        Assert.Equal(critic, result.Metadata.Fields["critic"]); Assert.Equal("Metacritic", result.Metadata.CriticProvider);
        Assert.Equal(2, reads.Count); Assert.Equal("/items/" + slug, reads[1].AbsolutePath);
        Assert.Equal(AllPlatforms, QueryHelpers.ParseQuery(reads[0].Query)["platforms"].ToString());
        if (completion == "N/A") Assert.DoesNotContain("completion", result.Metadata.Fields.Keys);
        else Assert.Equal(44, result.Metadata.Fields["completion"]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Old_default_platform_search_and_game_cache_entries_do_not_mask_the_fixed_catalog_scope(bool oldExactMatch)
    {
        var reads = 0;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var identity = new GameMetadataIdentityDto { Source = "dekudeals", Name = "Amber Trail", DekuDealsSlug = "amber-trail", Url = "https://www.dekudeals.com/items/amber-trail" };
        cache.Set("deku-metadata-v2-search:Amber Trail", oldExactMatch ? new List<GameMetadataIdentityDto> { identity } : [], TimeSpan.FromHours(24));
        cache.Set("deku-metadata-v2-game:amber-trail", new PublicGameMetadata { Identity = identity, Fields = new() { ["story"] = 1 }, SourceHours = new() { ["story"] = 1 } }, TimeSpan.FromHours(24));
        using var http = new HttpClient(new Handler((request, _) => { reads++; return Task.FromResult(Html(request.RequestUri!.AbsolutePath == "/search" ? Search : Page())); }));
        var result = await Provider(http, cache).LookupAsync("Amber Trail", null, default);
        Assert.NotNull(result.Metadata); Assert.Equal(24, result.Metadata.Fields["story"]); Assert.Equal(2, reads);
    }

    [Fact]
    public async Task A_game_title_is_encoded_as_one_query_value_and_cannot_change_the_platform_scope()
    {
        const string name = "Amber & platforms=switch ? q=other #fragment";
        var reads = 0;
        using var http = new HttpClient(new Handler((request, _) =>
        {
            reads++;
            var query = QueryHelpers.ParseQuery(request.RequestUri!.Query);
            Assert.Equal(2, query.Count); Assert.Equal(name, query["q"].ToString());
            Assert.True(query.TryGetValue("platforms", out var platforms)); Assert.Equal(AllPlatforms, platforms.ToString());
            Assert.Equal("", request.RequestUri.Fragment);
            return Task.FromResult(Html("<main></main>"));
        }));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await Provider(http, cache).LookupAsync(name, null, default);
        Assert.Null(result.Metadata); Assert.Empty(result.Matches); Assert.Equal(1, reads);
    }

    [Fact]
    public async Task Similar_editions_require_a_choice_and_arbitrary_slugs_are_never_requested()
    {
        var reads = new List<string>(); const string search = "<main><a class='main-link' href='/items/amber-remaster'>Amber Remaster</a><a class='main-link' href='/items/amber-definitive'>Amber Definitive</a><a class='main-link' href='https://untrusted.test/items/amber'>Amber</a></main>";
        using var http = new HttpClient(new Handler((request, _) => { reads.Add(request.RequestUri!.AbsolutePath); return Task.FromResult(Html(request.RequestUri.AbsolutePath == "/search" ? search : Page(slug: "amber-definitive", title: "Amber Definitive"))); })); using var cache = new MemoryCache(new MemoryCacheOptions()); var provider = Provider(http, cache);
        var unchosen = await provider.LookupAsync("Amber", null, default); Assert.Null(unchosen.Metadata); Assert.Equal(2, unchosen.Matches.Count); Assert.Single(reads);
        Assert.Null((await provider.LookupAsync("Amber", "not-in-search-results", default)).Metadata); Assert.Single(reads);
        var chosen = await provider.LookupAsync("Amber", "amber-definitive", default); Assert.Equal("Amber Definitive", chosen.Metadata!.Identity.Name); Assert.Equal("/items/amber-definitive", reads.Last());
    }

    [Theory]
    [InlineData("../private")]
    [InlineData("https://untrusted.test")]
    [InlineData("amber?url=http://127.0.0.1")]
    [InlineData("amber%2fother")]
    [InlineData("")]
    public async Task Rejects_unsafe_identity_before_any_HTTP_request(string slug)
    {
        using var http = new HttpClient(new Handler((_, _) => throw new InvalidOperationException("Unsafe identity must not read any URL"))); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await Provider(http, cache).LookupAsync("Amber", slug, default); Assert.Empty(result.Matches); Assert.Null(result.Metadata);
    }

    [Theory]
    [InlineData("Unknown")]
    [InlineData("0 hours")]
    [InlineData("-1 hours")]
    [InlineData("999999 hours")]
    [InlineData("1.5½ hours")]
    public async Task Missing_or_malformed_estimates_are_not_guessed_from_extras_or_other_numbers(string value)
    {
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Html(request.RequestUri!.AbsolutePath == "/search" ? Search : Page(value, "N/A"))))); using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.Null((await Provider(http, cache).LookupAsync("Amber Trail", null, default)).Metadata);
    }

    [Theory]
    [InlineData("title")]
    [InlineData("canonical")]
    [InlineData("attribution")]
    public async Task A_different_page_or_unattributed_numbers_cannot_supply_a_duration(string mismatch)
    {
        var page = mismatch == "title" ? Page(title: "Different game") : mismatch == "canonical" ? Page(slug: "different-game") : Page().Replace("https://howlongtobeat.com/game.php?id=12345", "https://untrusted.test/game.php?id=12345");
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Html(request.RequestUri!.AbsolutePath == "/search" ? Search : page)))); using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.Null((await Provider(http, cache).LookupAsync("Amber Trail", null, default)).Metadata);
    }

    [Theory]
    [InlineData(403)]
    [InlineData(429)]
    public async Task Blocks_and_retry_after_are_respected_without_retries_or_alternative_hosts(int status)
    {
        var calls = 0; var clock = new Clock();
        using var http = new HttpClient(new Handler((_, _) => { calls++; return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2)) } }); })); using var cache = new MemoryCache(new MemoryCacheOptions()); var provider = Provider(http, cache, clock);
        Assert.True((await provider.LookupAsync("First title", null, default)).Unavailable); Assert.True((await provider.LookupAsync("Second title", null, default)).Unavailable); Assert.Equal(1, calls);
        clock.Now += TimeSpan.FromMinutes(2); Assert.True((await provider.LookupAsync("Third title", null, default)).Unavailable); Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejects_oversized_HTML_by_declared_length_or_stream_bounds(bool declared)
    {
        using var http = new HttpClient(new Handler((_, _) => { var response = Html(new string('x', 1_000_001)); if (!declared) response.Content.Headers.ContentLength = null; return Task.FromResult(response); })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await Provider(http, cache).LookupAsync("Oversized fixture", null, default); Assert.True(result.Unavailable); Assert.Null(result.Metadata);
    }

    [Fact]
    public async Task Cancellation_reaches_the_requested_HTTP_read()
    {
        using var cancellation = new CancellationTokenSource(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, token) => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); return Html(Search); })); using var cache = new MemoryCache(new MemoryCacheOptions());
        var pending = Provider(http, cache).LookupAsync("Cancellable fixture", null, cancellation.Token); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Theory]
    [InlineData("93", "8.5", 93)]
    [InlineData("0", "9.9", 0)]
    public async Task Reads_the_Metacritic_critic_badge_not_the_adjacent_user_score_even_without_HLTB(string critic, string user, int expected)
    {
        var page = Page("N/A", "N/A").Replace("</main>", $"<li class='list-group-item'><strong>Metacritic:</strong> <a class='metacritic' href='https://www.metacritic.com/game/amber-trail/'><span class='text-white p-1 rounded bg-success'>{critic}</span> <span class='p-1 text-success'>{user}</span></a></li></main>");
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Html(request.RequestUri!.AbsolutePath == "/search" ? Search : page)))); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await Provider(http, cache).LookupAsync("Amber Trail", null, default);
        Assert.NotNull(result.Metadata); Assert.Equal(expected, result.Metadata.Fields["critic"]); Assert.DoesNotContain("story", result.Metadata.Fields.Keys);
    }

    [Theory]
    [InlineData("TBD")]
    [InlineData("101")]
    [InlineData("8.5")]
    public async Task Missing_or_invalid_Metacritic_uses_the_attributed_OpenCritic_average_not_stars_or_recommendation_percent(string missing)
    {
        var page = Page("N/A", "N/A").Replace("</main>", $"<li><strong>Metacritic:</strong><a class='metacritic' href='https://www.metacritic.com/game/amber-trail/'><span class='text-white'>{missing}</span><span>9.9</span></a></li><li><strong>OpenCritic:</strong><a class='opencritic' href='https://opencritic.com/game/42/amber-trail'><div class='opencritic-tier opencritic-mighty'>&nbsp;</div>94</a></li><li>98% recommend, 4.7 stars</li></main>");
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Html(request.RequestUri!.AbsolutePath == "/search" ? Search : page)))); using var cache = new MemoryCache(new MemoryCacheOptions());
        var result = await Provider(http, cache).LookupAsync("Amber Trail", null, default);
        Assert.NotNull(result.Metadata); Assert.Equal(94, result.Metadata.Fields["critic"]);
        Assert.Equal("OpenCritic", result.Metadata.CriticProvider); Assert.Equal("https://opencritic.com/game/42/amber-trail", result.Metadata.CriticUrl);
    }

    [Fact]
    public async Task Explicit_refresh_bypasses_parsed_cache_but_never_bypasses_a_provider_cooldown()
    {
        var reads = 0; var blocked = false;
        using var http = new HttpClient(new Handler((request, _) => { reads++; return Task.FromResult(blocked ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Headers = { RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromMinutes(2)) } } : Html(request.RequestUri!.AbsolutePath == "/search" ? Search : Page(reads < 3 ? "10 hours" : "20 hours"))); })); using var cache = new MemoryCache(new MemoryCacheOptions()); var provider = Provider(http, cache);
        Assert.Equal(10, (await provider.LookupAsync("Amber Trail", null, default)).Metadata!.Fields["story"]); Assert.Equal(10, (await provider.LookupAsync("Amber Trail", null, default)).Metadata!.Fields["story"]); Assert.Equal(2, reads);
        Assert.Equal(20, (await provider.LookupAsync("Amber Trail", null, default, forceRefresh: true)).Metadata!.Fields["story"]); Assert.Equal(4, reads);
        blocked = true; Assert.True((await provider.LookupAsync("Amber Trail", null, default, forceRefresh: true)).Unavailable); Assert.Equal(5, reads);
        Assert.True((await provider.LookupAsync("Amber Trail", null, default, forceRefresh: true)).Unavailable); Assert.Equal(5, reads);
    }

    [Theory]
    [InlineData("https://untrusted.invalid/game/42/amber-trail")]
    [InlineData("https://opencritic.com@untrusted.invalid/game/42/amber-trail")]
    [InlineData("https://opencritic.com/game/42/amber-trail?redirect=http://127.0.0.1")]
    public async Task Untrusted_critic_links_cannot_become_attributed_scores(string url)
    {
        var page = Page("N/A", "N/A").Replace("</main>", $"<li><strong>OpenCritic:</strong><a class='opencritic' href='{url}'>94</a></li></main>");
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(Html(request.RequestUri!.AbsolutePath == "/search" ? Search : page)))); using var cache = new MemoryCache(new MemoryCacheOptions());
        Assert.Null((await Provider(http, cache).LookupAsync("Amber Trail", null, default)).Metadata);
    }
}
