using System.Text.Json;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Services.Steam;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class SteamDateReviewTests(GamesDatabaseApiFactory factory) : IClassFixture<GamesDatabaseApiFactory>
{
    private static async Task<Game> Seed(GamesDbContext db)
    {
        var user = new User { Username = "Steam-date-fixture-" + Guid.NewGuid().ToString("N"), SteamId = "76561198000000000" }; db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Owned backlog" }; db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var game = new Game { UserId = user.Id, StatusId = status.Id, Name = "Date review fixture", SteamAppId = 620 }; db.Games.Add(game); await db.SaveChangesAsync(); return game;
    }
    private static SteamProfileService Service(GamesDbContext db, params SteamOwnedGameDto[] owned) => new(db, new Api(owned), null!, new MemoryCache(new MemoryCacheOptions()), NullLogger<SteamProfileService>.Instance);

    [Fact]
    public async Task Cached_achievement_evidence_supports_both_dates_when_another_accounts_API_key_omits_last_played_and_1970_is_ignored()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db);
        foreach (var date in new[] { DateTime.UnixEpoch.AddSeconds(1), new DateTime(2026, 1, 4), new DateTime(2026, 6, 2) }) db.SteamAchievements.Add(new() { UserId = game.UserId, GameId = game.Id, SteamAppId = 620, ApiName = Guid.NewGuid().ToString("N"), DisplayName = "Date fixture", Achieved = true, UnlockTime = date });
        await db.SaveChangesAsync(); var before = game.UpdatedAt;
        var suggestion = Assert.Single((await Service(db).GetDateSuggestionsAsync(game.UserId, game.Id, true, true))!);
        Assert.Equal("2026-01-04", suggestion.ProposedStarted); Assert.Equal("2026-06-02", suggestion.ProposedFinished); Assert.Equal("firstAchievement", suggestion.StartedSource); Assert.Equal("lastAchievement", suggestion.FinishedSource); Assert.Null(suggestion.ObservedLastPlayed); Assert.Contains("lastAchievementFallback", suggestion.Notes); Assert.Equal(before, game.UpdatedAt); Assert.Null(game.Started); Assert.Null(game.Finished);
        var legacy = Assert.Single((await Service(db).GetDateSuggestionsAsync(game.UserId, game.Id, true))!); Assert.Null(legacy.ProposedFinished); Assert.Equal("none", legacy.FinishedSource);
    }

    [Fact]
    public async Task Existing_curated_dates_are_not_replaced_or_advertised_as_missing_even_if_previously_Steam_managed()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db); game.Started = "2025-01-01"; game.Finished = "2025-02-01"; game.SteamFinishedSource = "steam"; await db.SaveChangesAsync();
        var service = Service(db, new SteamOwnedGameDto { AppId = 620, Name = "Fixture", LastPlayedAt = new DateTime(2026, 6, 1) }); Assert.Empty((await service.GetDateSuggestionsAsync(game.UserId, game.Id, true))!);
        var result = await service.ApplyDateSuggestionsAsync(game.UserId, new() { Suggestions = [new() { GameId = game.Id, Started = "2026-01-01", Finished = "2026-06-01", ExpectedUpdatedAt = game.UpdatedAt }] }); Assert.Equal(0, result!.Updated); Assert.Equal("2025-01-01", game.Started); Assert.Equal("2025-02-01", game.Finished); Assert.NotEmpty(result.Skipped);
    }

    [Theory]
    [InlineData("1970-01-01", null)] [InlineData("not-a-date", null)] [InlineData("2026-02-30", null)] [InlineData("2026-06-01", "2026-01-01")] [InlineData("2099-01-01", null)]
    public async Task Invalid_or_reversed_Steam_date_pairs_are_never_saved(string started, string? finished)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db);
        var result = await Service(db).ApplyDateSuggestionsAsync(game.UserId, new() { Suggestions = [new() { GameId = game.Id, Started = started, Finished = finished, ExpectedUpdatedAt = game.UpdatedAt }] });
        Assert.Equal(0, result!.Updated); Assert.Null(game.Started); Assert.Null(game.Finished); Assert.Contains(result.Skipped, item => item.GameId == game.Id && item.Reason == "invalidDate");
    }

    [Fact]
    public async Task A_stale_review_does_not_overwrite_a_concurrently_changed_game()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db);
        var result = await Service(db).ApplyDateSuggestionsAsync(game.UserId, new() { Suggestions = [new() { GameId = game.Id, Started = "2026-01-04", ExpectedUpdatedAt = game.UpdatedAt.AddDays(-1) }] });
        Assert.Equal(0, result!.Updated); Assert.Null(game.Started); Assert.Contains(result.Skipped, item => item.Reason == "changed");
    }

    [Fact]
    public async Task An_explicit_valid_apply_returns_exact_field_receipts_and_history_then_is_idempotent()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db); var service = Service(db);
        var request = new SteamApplyDateSuggestionsRequest { Suggestions = [new() { GameId = game.Id, Started = "2026-01-04", Finished = "2026-06-02", ExpectedUpdatedAt = game.UpdatedAt }] };
        var result = await service.ApplyDateSuggestionsAsync(game.UserId, request); Assert.Equal(1, result!.Updated); var applied = Assert.Single(result.Applied); Assert.Equal(game.Id, applied.GameId); Assert.Equal("2026-01-04", applied.Started); Assert.Equal("2026-06-02", applied.Finished); Assert.Equal("steam-reviewed", game.SteamFinishedSource);
        Assert.Equal(2, await db.GameHistoryEntries.CountAsync(entry => entry.UserId == game.UserId && entry.GameId == game.Id)); Assert.Equal(0, (await service.ApplyDateSuggestionsAsync(game.UserId, request))!.Updated);
    }

    [Fact]
    public async Task A_later_Steam_sync_never_moves_a_saved_finish_date_into_a_different_year()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db);
        game.Finished = "2025-02-01"; game.SteamFinishedSource = "steam"; game.SteamFinishedLastValue = game.Finished; game.Critic = 90; game.Logo = "https://images.test/logo.png"; game.Hero = "https://images.test/hero.jpg"; game.Cover = "https://images.test/cover.jpg"; await db.SaveChangesAsync();
        var api = new Api([new() { AppId = 620, Name = "Fixture", LastPlayedAt = new DateTime(2026, 6, 1) }]); var sync = new SteamSyncService(db, api, null!, NullLogger<SteamSyncService>.Instance, new(db, api));
        await sync.SyncGameAsync(game.UserId, game.Id); Assert.Equal("2025-02-01", game.Finished); Assert.Equal("2025-02-01", game.SteamFinishedLastValue);
    }

    [Theory]
    [InlineData("Europe/Madrid", "2026-01-01")]
    [InlineData("America/Los_Angeles", "2025-12-31")]
    [InlineData("UTC", "2025-12-31")]
    public async Task Reference_dates_use_the_review_timezone_at_midnight_without_moving_saved_calendar_dates(string timeZone, string expected)
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db);
        db.SteamAchievements.Add(new() { UserId = game.UserId, GameId = game.Id, SteamAppId = 620, ApiName = "New-year", DisplayName = "New-year", Achieved = true, UnlockTime = new DateTime(2025, 12, 31, 23, 30, 0, DateTimeKind.Utc) }); await db.SaveChangesAsync();
        var service = Service(db); var reference = Assert.Single((await service.GetDateSuggestionsAsync(game.UserId, game.Id, true, true, timeZone))!); Assert.Equal(expected, reference.ProposedStarted); Assert.Equal(timeZone, reference.ReviewTimeZone);
        var result = await service.ApplyDateSuggestionsAsync(game.UserId, new() { TimeZone = timeZone, Suggestions = [new() { GameId = game.Id, Started = reference.ProposedStarted, ExpectedUpdatedAt = reference.UpdatedAt }] }); Assert.Equal(1, result!.Updated); Assert.Equal(expected, game.Started);
    }

    [Fact]
    public async Task References_from_another_playthrough_never_contradict_saved_counterpart_dates()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db);
        db.SteamAchievements.Add(new() { UserId = game.UserId, GameId = game.Id, SteamAppId = 620, ApiName = "Earlier-play", DisplayName = "Earlier-play", Achieved = true, UnlockTime = new DateTime(2026, 6, 2) }); game.Started = "2026-09-01"; await db.SaveChangesAsync();
        Assert.Empty((await Service(db).GetDateSuggestionsAsync(game.UserId, game.Id, true, true))!); Assert.Equal("2026-09-01", game.Started);
        game.Started = null; game.Finished = "2025-02-01"; await db.SaveChangesAsync(); Assert.Empty((await Service(db).GetDateSuggestionsAsync(game.UserId, game.Id, true, true))!); Assert.Equal("2025-02-01", game.Finished);
    }

    [Fact]
    public async Task Steam_progress_wire_dates_are_explicit_UTC_and_unknown_unlocks_never_render_as_1970()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<GamesDbContext>(); var game = await Seed(db);
        game.SteamAchievementsSyncedAt = new DateTime(2026, 6, 2, 23, 30, 0); await db.SaveChangesAsync();
        foreach (var date in new[] { DateTime.UnixEpoch.AddSeconds(1), new DateTime(2026, 6, 2, 23, 30, 0) }) db.SteamAchievements.Add(new() { UserId = game.UserId, GameId = game.Id, SteamAppId = 620, ApiName = Guid.NewGuid().ToString("N"), DisplayName = "Wire date", Achieved = true, UnlockTime = date });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); var progress = (await Service(db).GetAchievementProgressAsync(game.UserId, game.Id))!;
        Assert.Equal(DateTimeKind.Utc, progress.LastSynced!.Value.Kind); Assert.Single(progress.Achievements, row => row.UnlockTime.HasValue); Assert.Equal(DateTimeKind.Utc, progress.Achievements.Single(row => row.UnlockTime.HasValue).UnlockTime!.Value.Kind);
        var wire = JsonSerializer.SerializeToElement(progress, new JsonSerializerOptions(JsonSerializerDefaults.Web)); Assert.EndsWith("Z", wire.GetProperty("lastSynced").GetString()!); Assert.DoesNotContain("1970", wire.GetRawText());
    }

    private sealed class Api(SteamOwnedGameDto[] owned) : ISteamApiService
    {
        public Task<List<SteamOwnedGameDto>> GetOwnedGamesAsync(string steamId) => Task.FromResult(owned.ToList());
        public Task<SteamPlayerAchievementsResult> GetPlayerAchievementsAsync(string steamId, int appId) => throw new NotSupportedException();
        public Task<SteamGameSchemaDto?> GetGameSchemaAsync(int appId) => throw new NotSupportedException();
        public Task<SteamPlayerSummaryDto?> GetPlayerSummaryAsync(string steamId) => throw new NotSupportedException();
        public Task<SteamAchievementSnapshot> GetAchievementSnapshotAsync(string steamId, int appId) => Task.FromResult(new SteamAchievementSnapshot { Error = "statsUnavailable" });
    }
}
