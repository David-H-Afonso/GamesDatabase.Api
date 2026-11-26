using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services.Steam;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class SteamArtworkTests
{
    private sealed class Store : ISteamStoreService
    {
        public SteamAssetUrlsDto? Assets { get; set; }
        public SteamAppDetailsDto? Details { get; set; }
        public string? Icon { get; set; }
        public Action? OnAssets { get; set; }
        public int AssetCalls { get; private set; }
        public int DetailCalls { get; private set; }
        public int IconCalls { get; private set; }
        public Task<SteamAssetUrlsDto?> GetAssetUrlsAsync(int appId, CancellationToken cancellationToken = default) { AssetCalls++; OnAssets?.Invoke(); return Task.FromResult(Assets); }
        public Task<SteamAppDetailsDto?> GetOrCacheAppDetailsAsync(int appId) => throw new InvalidOperationException("Artwork must not populate the persistent metadata cache.");
        public Task<SteamAppDetailsDto?> GetAppDetailsAsync(int appId, CancellationToken cancellationToken = default) { DetailCalls++; return Task.FromResult(Details); }
        public Task<string?> GetCommunityIconUrlAsync(int appId, CancellationToken cancellationToken = default) { IconCalls++; return Task.FromResult(Icon); }
        public Task<SteamReviewSummaryDto?> GetReviewSummaryAsync(int appId) => Task.FromResult<SteamReviewSummaryDto?>(null);
        public Task<List<SteamStoreSearchItemDto>> SearchStoreAsync(string query) => Task.FromResult(new List<SteamStoreSearchItemDto>());
        public Task<string?> GetLibraryCoverUrlAsync(int appId) => Task.FromResult(Assets?.CoverUrl);
    }

    [Fact]
    public async Task Resolved_hashed_roles_are_returned_without_changing_provider_objects_or_syncing_a_game()
    {
        var assets = new SteamAssetUrlsDto { LogoUrl = "https://shared.akamai.steamstatic.com/community_assets/images/apps/100/hash.jpg", HeroUrl = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/100/content-hash/header.jpg", CoverUrl = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/100/content-hash/library_600x900.jpg" };
        var store = new Store { Assets = assets };
        var result = await new SteamArtworkService(store).GetAsync(100);
        Assert.NotSame(assets, result);
        Assert.Equal(assets.HeroUrl, result!.HeroUrl);
        Assert.Equal(assets.CoverUrl, result.CoverUrl);
        Assert.Equal(assets.LogoUrl, result.LogoUrl);
        Assert.Equal(1, store.AssetCalls);
        Assert.Equal(0, store.DetailCalls);
        Assert.Equal(0, store.IconCalls);
    }

    [Fact]
    public async Task Missing_logo_and_hero_use_the_original_provider_fallbacks_but_do_not_mutate_cached_assets()
    {
        var assets = new SteamAssetUrlsDto { CoverUrl = "https://cdn.test/cover.jpg" };
        var store = new Store { Assets = assets, Icon = "https://cdn.test/icon.jpg", Details = new() { HeaderImageUrl = "https://cdn.test/real-hash/header.jpg" } };
        var result = await new SteamArtworkService(store).GetAsync(100);
        Assert.Equal("https://cdn.test/icon.jpg", result!.LogoUrl);
        Assert.Equal("https://cdn.test/real-hash/header.jpg", result.HeroUrl);
        Assert.Equal(assets.CoverUrl, result.CoverUrl);
        Assert.Null(assets.LogoUrl);
        Assert.Null(assets.HeroUrl);
    }

    [Fact]
    public async Task A_cover_cannot_be_substituted_for_missing_hero_or_logo_and_urls_are_not_guessed()
    {
        var store = new Store { Assets = new() { CoverUrl = "https://cdn.test/cover.jpg" } };
        var result = await new SteamArtworkService(store).GetAsync(100);
        Assert.Null(result!.HeroUrl);
        Assert.Null(result.LogoUrl);
        Assert.Equal("https://cdn.test/cover.jpg", result.CoverUrl);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///private/image.png")]
    [InlineData("https://user:password@cdn.test/image.png")]
    [InlineData("not-a-url")]
    public async Task Unusable_or_credentialed_provider_urls_are_not_exposed_as_candidates(string url)
    {
        var store = new Store { Assets = new() { LogoUrl = url, HeroUrl = url, CoverUrl = url }, Icon = url, Details = new() { HeaderImageUrl = url } };
        Assert.Null(await new SteamArtworkService(store).GetAsync(100));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Invalid_app_ids_do_not_contact_providers(int id)
    {
        var store = new Store();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new SteamArtworkService(store).GetAsync(id));
        Assert.Equal(0, store.AssetCalls);
    }

    [Fact]
    public async Task Cancellation_prevents_fallback_requests()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new Store { OnAssets = cancellation.Cancel };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SteamArtworkService(store).GetAsync(100, cancellation.Token));
        Assert.Equal(0, store.IconCalls);
        Assert.Equal(0, store.DetailCalls);
    }
}

public sealed class SteamArtworkEndpointTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private sealed class Artwork : ISteamArtworkService
    {
        public int Calls { get; private set; }
        public Task<SteamAssetUrlsDto?> GetAsync(int appId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult<SteamAssetUrlsDto?>(new() { HeroUrl = "https://cdn.test/content-hash/header.jpg" });
        }
    }

    [Fact]
    public async Task Candidate_endpoint_is_authenticated_read_only_and_non_cacheable()
    {
        var artwork = new Artwork();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<ISteamArtworkService>(); services.AddSingleton<ISteamArtworkService>(artwork); }));
        var client = app.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/steam/app/100/artwork")).StatusCode);
        Assert.Equal(0, artwork.Calls);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateWebTokenAsync("HouseholdUserA"));
        var response = await client.GetAsync("/api/steam/app/100/artwork");
        response.EnsureSuccessStatusCode();
        Assert.Equal("https://cdn.test/content-hash/header.jpg", (await response.Content.ReadFromJsonAsync<SteamAssetUrlsDto>())!.HeroUrl);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Equal(1, artwork.Calls);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.PostAsJsonAsync("/api/steam/app/100/artwork", new { })).StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task Invalid_route_ids_fail_before_provider_resolution(int id)
    {
        var artwork = new Artwork();
        using var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services => { services.RemoveAll<ISteamArtworkService>(); services.AddSingleton<ISteamArtworkService>(artwork); }));
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateWebTokenAsync("HouseholdUserA"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/steam/app/{id}/artwork")).StatusCode);
        Assert.Equal(0, artwork.Calls);
    }
}

public sealed class SteamArtworkProviderTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }
    private sealed class Clients(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Even_header_fallback_does_not_read_or_write_the_persistent_metadata_cache_or_games(bool existingCache)
    {
        await factory.EnsureSeededAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
        var id = existingCache ? 111222 : 111223;
        if (existingCache)
        {
            db.SteamAppCaches.Add(new() { AppId = id, Name = "Keep original cached metadata", HeaderImageUrl = "https://cdn.test/cached.jpg", LastFetched = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        var beforeCache = JsonSerializer.Serialize(await db.SteamAppCaches.AsNoTracking().OrderBy(item => item.AppId).ToListAsync());
        var beforeGames = JsonSerializer.Serialize(await db.Games.AsNoTracking().OrderBy(game => game.Id).ToListAsync());
        using var http = new HttpClient(new Handler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.Contains("appdetails")
                ? JsonContent.Create(new Dictionary<string, object> { [id.ToString()] = new { success = true, data = new { name = "Fresh provider name", header_image = "https://cdn.test/fresh-hash/header.jpg" } } })
                : new StringContent("{}")
        })));
        var store = new SteamStoreService(new Clients(http), Options.Create(new SteamSettings()), db, NullLogger<SteamStoreService>.Instance);
        var result = await new SteamArtworkService(store).GetAsync(id);
        Assert.Equal("https://cdn.test/fresh-hash/header.jpg", result!.HeroUrl);
        Assert.Null(result.CoverUrl);
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(beforeCache, JsonSerializer.Serialize(await db.SteamAppCaches.AsNoTracking().OrderBy(item => item.AppId).ToListAsync()));
        Assert.Equal(beforeGames, JsonSerializer.Serialize(await db.Games.AsNoTracking().OrderBy(game => game.Id).ToListAsync()));
    }

    [Theory]
    [InlineData("assets")]
    [InlineData("icon")]
    [InlineData("metadata")]
    public async Task Request_cancellation_reaches_the_provider_HTTP_request(string method)
    {
        await factory.EnsureSeededAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new Handler(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }));
        var store = new SteamStoreService(new Clients(http), Options.Create(new SteamSettings()), scope.ServiceProvider.GetRequiredService<GamesDbContext>(), NullLogger<SteamStoreService>.Instance);
        Task request = method switch
        {
            "assets" => store.GetAssetUrlsAsync(100, cancellation.Token),
            "icon" => store.GetCommunityIconUrlAsync(100, cancellation.Token),
            _ => store.GetAppDetailsAsync(100, cancellation.Token)
        };
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
    }
}

public sealed class SteamAppIdEditorIntegrationTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    [Theory]
    [InlineData("same")]
    [InlineData("comment")]
    [InlineData("different")]
    [InlineData("unlink")]
    public async Task Only_a_saved_different_App_ID_invalidates_Steam_cache_while_preserving_personal_fields_and_artwork(string change)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await factory.CreateWebTokenAsync("HouseholdUserA"));
        int id;
        var syncedAt = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>();
            var owner = await db.Games.AsNoTracking().SingleAsync(game => game.Id == factory.UserAGameId);
            var game = new Game
            {
                UserId = owner.UserId, StatusId = owner.StatusId, Name = $"Editor-{change}", SteamAppId = 100,
                SteamPlaytimeForever = 600, SteamPlaytime2Weeks = 30, SteamLastSynced = syncedAt,
                SteamCompletionPreserved = true, SteamAchievementsSource = "sets", SteamAchievementsSyncedAt = syncedAt,
                SteamAchievementsLastError = "network", SteamAchievementSchemaVersion = 2, SteamAchievementSchemaHash = 123,
                ManualPlaytimeMinutes = 0, IsManuallyCompleted = true, Favorite = true, Comment = "Personal note",
                Released = "2020-01-01", Started = "2021-01-01", Finished = "2021-03-01",
                Logo = "https://cdn.test/logo.jpg", Hero = "https://cdn.test/hero.jpg", Cover = "https://cdn.test/cover.jpg"
            };
            db.Games.Add(game); await db.SaveChangesAsync(); id = game.Id;
            db.SteamAchievements.Add(new() { UserId = game.UserId, GameId = id, SteamAppId = 100, ApiName = $"KEEP_UNLOCK_{change}", Achieved = true });
            await db.SaveChangesAsync();
        }
        var patch = change == "comment" ? new Dictionary<string, object?> { ["comment"] = "Updated note" }
            : new Dictionary<string, object?> { ["steamAppId"] = change == "unlink" ? null : change == "same" ? 100 : 200 };
        (await client.PutAsJsonAsync($"/api/games/{id}", patch)).EnsureSuccessStatusCode();
        await using var after = factory.Services.CreateAsyncScope();
        var context = after.ServiceProvider.GetRequiredService<GamesDbContext>();
        var saved = await context.Games.AsNoTracking().SingleAsync(game => game.Id == id);
        var changed = change is "different" or "unlink";
        Assert.Equal(changed ? (int?)null : 600, saved.SteamPlaytimeForever);
        Assert.Equal(changed ? (int?)null : 30, saved.SteamPlaytime2Weeks);
        Assert.Equal(changed ? (DateTime?)null : syncedAt, saved.SteamLastSynced);
        Assert.Equal(!changed, saved.SteamCompletionPreserved);
        Assert.Equal(changed ? null : "sets", saved.SteamAchievementsSource);
        Assert.Equal(changed ? (DateTime?)null : syncedAt, saved.SteamAchievementsSyncedAt);
        Assert.Equal(changed ? null : "network", saved.SteamAchievementsLastError);
        Assert.Equal(changed ? (int?)null : 2, saved.SteamAchievementSchemaVersion);
        Assert.Equal(changed ? (long?)null : 123, saved.SteamAchievementSchemaHash);
        Assert.Equal(0, saved.ManualPlaytimeMinutes); Assert.True(saved.IsManuallyCompleted); Assert.True(saved.Favorite);
        Assert.Equal(change == "comment" ? "Updated note" : "Personal note", saved.Comment);
        Assert.Equal("2020-01-01", saved.Released); Assert.Equal("2021-01-01", saved.Started); Assert.Equal("2021-03-01", saved.Finished);
        Assert.Equal("https://cdn.test/logo.jpg", saved.Logo); Assert.Equal("https://cdn.test/hero.jpg", saved.Hero); Assert.Equal("https://cdn.test/cover.jpg", saved.Cover);
        Assert.Equal(1, await context.SteamAchievements.CountAsync(achievement => achievement.GameId == id && achievement.Achieved));
    }
}
