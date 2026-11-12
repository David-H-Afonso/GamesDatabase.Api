using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Application.Services.Steam;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class SteamAchievementSyncTests
{
    [Fact]
    public async Task Legacy_100_percent_survives_new_locked_dlc_without_claiming_current_100_percent()
    {
        await using var db = await TestDatabase.CreateAsync();
        db.Context.SteamAchievements.AddRange(db.Achievement("ACH_0", true), db.Achievement("ACH_1", true));
        await db.Context.SaveChangesAsync();
        var snapshot = SteamAchievementFixtures.Snapshot([2, 1], [2, 0], 0);
        snapshot.Player!.Groups![1].IsAchievable = false;

        await db.SyncAsync(snapshot);
        var dto = db.Game.ToDto();
        await SteamAchievementProgress.FillGamesAsync(db.Context, [dto], db.Game.UserId);

        Assert.True(db.Game.SteamCompletionPreserved);
        Assert.True(dto.IsCompleted);
        Assert.False(dto.IsFullyCompleted);
        Assert.Equal(2, dto.SteamAchievementsUnlocked);
        Assert.Equal(3, dto.SteamAchievementsTotal);
        Assert.Single(await SteamAchievementProgress.ApplyFilters(db.Context.Games, db.Context, db.Game.UserId, true, null).ToListAsync());
        Assert.Empty(await SteamAchievementProgress.ApplyFilters(db.Context.Games, db.Context, db.Game.UserId, null, true).ToListAsync());
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var profile = new SteamProfileService(db.Context, new SnapshotApi(snapshot), null!, cache, NullLogger<SteamProfileService>.Instance);
        var detail = await profile.GetAchievementProgressAsync(db.Game.UserId, db.Game.Id);
        Assert.Equal(2, detail!.EligibleAchievementsTotal);
        Assert.Equal(3, detail.TotalAchievements);
        Assert.False(detail.Sets.Single(set => set.DlcAppId.HasValue).IsAchievable);
    }

    [Fact]
    public async Task Vampire_survivors_style_completed_sets_qualify_at_197_of_243()
    {
        await using var db = await TestDatabase.CreateAsync();
        var snapshot = SteamAchievementFixtures.Snapshot([140, 30, 30, 43], [140, 30, 20, 7], 0, 1, 2, 3);
        await db.SyncAsync(snapshot);
        var dto = db.Game.ToDto();
        await SteamAchievementProgress.FillGamesAsync(db.Context, [dto], db.Game.UserId);

        Assert.True(dto.IsCompleted);
        Assert.False(dto.IsFullyCompleted);
        Assert.Equal(197, dto.SteamAchievementsUnlocked);
        Assert.Equal(243, dto.SteamAchievementsTotal);
        Assert.Equal(4, dto.SteamSetsCompleted);
        Assert.Single(await SteamAchievementProgress.ApplyFilters(db.Context.Games, db.Context, db.Game.UserId, true, null).ToListAsync());
        Assert.Empty(await SteamAchievementProgress.ApplyFilters(db.Context.Games, db.Context, db.Game.UserId, null, true).ToListAsync());
    }

    [Fact]
    public async Task Archived_locked_achievements_do_not_block_100_percent_and_earned_ones_remain_visible()
    {
        await using var db = await TestDatabase.CreateAsync();
        var snapshot = SteamAchievementFixtures.Snapshot([3], [2], 0);
        snapshot.Schema!.Achievements![2].Archived = true;
        snapshot.Schema.Groups![0].CompletionAchievements = 2;
        await db.SyncAsync(snapshot);

        using var cache = new MemoryCache(new MemoryCacheOptions());
        var profile = new SteamProfileService(db.Context, new SnapshotApi(snapshot), null!, cache, NullLogger<SteamProfileService>.Instance);
        var detail = await profile.GetAchievementProgressAsync(db.Game.UserId, db.Game.Id);
        Assert.True(detail!.IsFullyCompleted);
        Assert.Equal(2, detail.TotalAchievements);
        Assert.Equal(2, detail.Achievements.Count);

        snapshot.Player!.Achievements!.Add(new() { InternalKey = 2, Unlocked = true, UnlockTime = 1700000010 });
        await db.SyncAsync(snapshot);
        snapshot.Player.Achievements.RemoveAt(2); // Archived entry omitted by a later API response.
        await db.SyncAsync(snapshot);
        detail = await profile.GetAchievementProgressAsync(db.Game.UserId, db.Game.Id);
        Assert.True(detail!.IsFullyCompleted);
        Assert.Equal(3, detail.Achievements.Count);
        Assert.True(detail.Achievements.Single(achievement => achievement.Archived).Achieved);
    }

    [Fact]
    public async Task Completed_set_trophy_and_unlock_identity_survive_new_achievements_and_repeat_syncs()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.SyncAsync(SteamAchievementFixtures.Snapshot([1], [1], 0));
        var originalId = (await db.Context.SteamAchievements.SingleAsync()).Id;
        var completedAt = (await db.Context.SteamAchievementSets.SingleAsync()).CompletedAt;
        var expanded = SteamAchievementFixtures.Snapshot([2], [1]);
        await db.SyncAsync(expanded);
        await db.SyncAsync(expanded);

        Assert.Equal(2, await db.Context.SteamAchievements.CountAsync());
        Assert.Single(await db.Context.SteamAchievementSets.ToListAsync());
        Assert.Equal(originalId, (await db.Context.SteamAchievements.SingleAsync(achievement => achievement.ApiName == "ACH_0")).Id);
        var set = await db.Context.SteamAchievementSets.SingleAsync();
        Assert.True(set.IsCompleted);
        Assert.Equal(completedAt, set.CompletedAt);
        var dto = db.Game.ToDto();
        await SteamAchievementProgress.FillGamesAsync(db.Context, [dto], db.Game.UserId);
        Assert.True(dto.IsCompleted);
        Assert.False(dto.IsFullyCompleted);
    }

    [Theory]
    [InlineData("profilePrivate")]
    [InlineData("rateLimited")]
    [InlineData("schemaChanged")]
    [InlineData("steamUnavailable")]
    public async Task Failed_sync_preserves_all_cached_unlocks_metadata_and_success_timestamp(string error)
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.SyncAsync(SteamAchievementFixtures.Snapshot([2], [1]));
        var saved = await db.Context.SteamAchievements.SingleAsync(achievement => achievement.Achieved);
        var unlockTime = saved.UnlockTime;
        var syncedAt = db.Game.SteamAchievementsSyncedAt;

        var result = await db.SyncAsync(new() { Error = error });

        Assert.False(result.Success);
        Assert.True(saved.Achieved);
        Assert.Equal(unlockTime, saved.UnlockTime);
        Assert.Equal(syncedAt, db.Game.SteamAchievementsSyncedAt);
        Assert.Equal(error, db.Game.SteamAchievementsLastError);
        Assert.Equal(2, await db.Context.SteamAchievements.CountAsync());
        Assert.Single(await db.Context.SteamAchievementSets.ToListAsync());
    }

    [Fact]
    public async Task Deleted_definitions_are_retired_without_deleting_unlock_history()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.SyncAsync(SteamAchievementFixtures.Snapshot([2], [2], 0));
        await db.SyncAsync(SteamAchievementFixtures.Snapshot([1], [1], 0));

        Assert.Equal(2, await db.Context.SteamAchievements.CountAsync());
        var retired = await db.Context.SteamAchievements.SingleAsync(achievement => achievement.ApiName == "ACH_1");
        Assert.False(retired.IsActive);
        Assert.True(retired.Achieved);
        Assert.NotNull(retired.UnlockTime);
        Assert.True(db.Game.SteamCompletionPreserved);
        Assert.Single(await SteamAchievementProgress.RequiredAchievements(db.Context, db.Game.UserId).ToListAsync());
    }

    [Fact]
    public async Task Confirmed_zero_achievements_is_not_vacuous_100_percent()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.SyncAsync(SteamAchievementFixtures.Snapshot([1], [1], 0));
        await db.SyncAsync(new() { Success = true, SupportsSets = true, NoAchievements = true });
        var dto = db.Game.ToDto();
        await SteamAchievementProgress.FillGamesAsync(db.Context, [dto], db.Game.UserId);
        Assert.True(dto.IsCompleted);
        Assert.False(dto.IsFullyCompleted);
        Assert.Equal(0, dto.SteamAchievementsTotal);
        Assert.True((await db.Context.SteamAchievements.SingleAsync()).Achieved);
    }

    [Fact]
    public async Task Legacy_fallback_does_not_flatten_existing_dlc_or_completed_sets()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.SyncAsync(SteamAchievementFixtures.Snapshot([1, 1], [1, 0], 0));
        await db.SyncAsync(new()
        {
            Success = true,
            LegacySchema = new() { Achievements = [new() { ApiName = "ACH_0", DisplayName = "Updated title" }, new() { ApiName = "ACH_1", DisplayName = "DLC" }] },
            LegacyAchievements = [new() { ApiName = "ACH_0", Achieved = 1, UnlockTime = 1700000000 }, new() { ApiName = "ACH_1", Achieved = 0 }]
        });
        Assert.Equal("sets", db.Game.SteamAchievementsSource);
        Assert.Equal("setsUnavailable", db.Game.SteamAchievementsLastError);
        Assert.True((await db.Context.SteamAchievementSets.SingleAsync(set => set.GroupId == 0)).IsCompleted);
        Assert.Equal(2001, (await db.Context.SteamAchievementSets.SingleAsync(set => set.GroupId == 1)).DlcAppId);
        Assert.Equal(1, (await db.Context.SteamAchievements.SingleAsync(achievement => achievement.ApiName == "ACH_1")).GroupId);
    }

    [Fact]
    public async Task Empty_and_developer_only_sets_never_grant_public_completion()
    {
        await using var db = await TestDatabase.CreateAsync();
        var empty = SteamAchievementFixtures.Snapshot([0], [0], 0);
        await db.SyncAsync(empty);
        Assert.False(db.Game.SteamCompletionPreserved);
        Assert.False((await db.Context.SteamAchievementSets.SingleAsync()).IsCompleted);

        var developer = SteamAchievementFixtures.Snapshot([1], [1], 0);
        developer.Schema!.Groups![0].DeveloperOnly = true;
        await db.SyncAsync(developer);
        var dto = db.Game.ToDto();
        await SteamAchievementProgress.FillGamesAsync(db.Context, [dto], db.Game.UserId);
        Assert.False(dto.IsCompleted);
        Assert.False(dto.IsFullyCompleted);
        Assert.Equal(0, dto.SteamAchievementsTotal);
        Assert.Empty(await SteamAchievementProgress.VisibleAchievements(db.Context, db.Game.UserId).ToListAsync());
    }

    [Fact]
    public async Task Partial_progress_and_relative_icons_are_normalized()
    {
        await using var db = await TestDatabase.CreateAsync();
        var snapshot = SteamAchievementFixtures.Snapshot([1], [0]);
        snapshot.Schema!.Achievements![0].ProgressType = 1;
        snapshot.Schema.Achievements[0].MinProgressInt = 0;
        snapshot.Schema.Achievements[0].MaxProgressInt = 10;
        snapshot.Player!.Achievements!.Add(new() { InternalKey = 0, ProgressInt = 7 });
        await db.SyncAsync(snapshot);
        var achievement = await db.Context.SteamAchievements.SingleAsync();
        Assert.False(achievement.Achieved);
        Assert.Equal(7, achievement.ProgressCurrent);
        Assert.Equal(10, achievement.ProgressMax);
        Assert.Equal("https://shared.fastly.steamstatic.com/community_assets/images/apps/620/achieved.jpg", achievement.IconUrl);
    }

    [Fact]
    public async Task Manual_non_steam_completion_is_retained_and_duration_100_is_never_completion()
    {
        await using var db = await TestDatabase.CreateAsync();
        db.Game.SteamAppId = null;
        db.Game.IsManuallyCompleted = true;
        await db.Context.SaveChangesAsync();
        Assert.Single(await SteamAchievementProgress.ApplyFilters(db.Context.Games, db.Context, db.Game.UserId, null, true).ToListAsync());
        db.Game.IsManuallyCompleted = false;
        db.Game.Completion = 100;
        await db.Context.SaveChangesAsync();
        Assert.Empty(await SteamAchievementProgress.ApplyFilters(db.Context.Games, db.Context, db.Game.UserId, true, null).ToListAsync());
    }

    [Fact]
    public async Task Filters_and_sync_never_use_another_users_progress()
    {
        await using var db = await TestDatabase.CreateAsync();
        var other = new User { Username = "Other", Role = UserRole.Standard };
        db.Context.Users.Add(other);
        await db.Context.SaveChangesAsync();
        var status = new GameStatus { UserId = other.Id, Name = "Other backlog" };
        db.Context.GameStatuses.Add(status);
        await db.Context.SaveChangesAsync();
        var game = new Game { UserId = other.Id, StatusId = status.Id, Name = "Other game", SteamAppId = 620 };
        db.Context.Games.Add(game);
        await db.Context.SaveChangesAsync();
        db.Context.SteamAchievements.Add(new() { UserId = other.Id, GameId = game.Id, SteamAppId = 620, ApiName = "ACH_0", Achieved = true });
        await db.Context.SaveChangesAsync();

        await db.SyncAsync(SteamAchievementFixtures.Snapshot([1], [0]));

        Assert.True((await db.Context.SteamAchievements.SingleAsync(achievement => achievement.UserId == other.Id)).Achieved);
        Assert.Empty(await SteamAchievementProgress.ApplyFilters(db.Context.Games, db.Context, db.Game.UserId, true, null).ToListAsync());
        var otherMatches = await SteamAchievementProgress.ApplyFilters(db.Context.Games, db.Context, other.Id, true, null).ToListAsync();
        Assert.Equal(game.Id, Assert.Single(otherMatches).Id);
    }

    [Fact]
    public async Task Incomplete_success_payload_cannot_replace_saved_definitions()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.SyncAsync(SteamAchievementFixtures.Snapshot([1], [1], 0));
        var result = await db.SyncAsync(new() { Success = true, SupportsSets = true });
        Assert.False(result.Success);
        Assert.Equal("invalidResponse", result.Error);
        Assert.True((await db.Context.SteamAchievements.SingleAsync()).IsActive);
        Assert.True(db.Game.SteamCompletionPreserved);
    }

    [Fact]
    public async Task Concurrent_user_syncs_are_serialized_through_database_commit()
    {
        await using var db = await TestDatabase.CreateAsync();
        var user = await db.Context.Users.SingleAsync();
        user.SteamId = "76561198000000000";
        db.Game.Critic = 90;
        db.Game.Logo = "https://images.test/logo.png";
        db.Game.Hero = "https://images.test/hero.jpg";
        db.Game.Cover = "https://images.test/cover.jpg";
        await db.Context.SaveChangesAsync();
        var options = new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(db.Context.Database.GetDbConnection()).Options;
        await using var firstContext = new GamesDbContext(options);
        await using var secondContext = new GamesDbContext(options);
        var api = new BlockingApi(SteamAchievementFixtures.Snapshot([1], [1], 0));
        SteamSyncService Service(GamesDbContext context) => new(context, api, null!, NullLogger<SteamSyncService>.Instance, new(context, api));
        var first = Service(firstContext).SyncGameAsync(user.Id, db.Game.Id);
        var second = Service(secondContext).SyncGameAsync(user.Id, db.Game.Id);
        var callsBeforeRelease = api.OwnedCalls;
        api.Release.TrySetResult();
        var results = await Task.WhenAll(first, second);

        Assert.Equal(1, callsBeforeRelease);
        Assert.All(results, result => Assert.True(result.Success));
        Assert.Single(await db.Context.SteamAchievements.AsNoTracking().ToListAsync());
        Assert.Single(await db.Context.SteamAchievementSets.AsNoTracking().ToListAsync());
        Assert.True((await db.Context.Games.AsNoTracking().SingleAsync()).SteamCompletionPreserved);
    }

    [Fact]
    public async Task Bulk_sync_commits_each_game_before_reporting_and_retains_it_on_cancellation()
    {
        await using var db = await TestDatabase.CreateAsync();
        var second = await PrepareBulkAsync(db);
        using var cancel = new CancellationTokenSource();
        var api = new SnapshotApi(SteamAchievementFixtures.Snapshot([1], [1], 0));
        var service = new SteamSyncService(db.Context, api, null!, NullLogger<SteamSyncService>.Instance, new(db.Context, api));
        var committedBeforeCancellation = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SyncAllUserGamesAsync(db.Game.UserId, progress =>
        {
            if (progress.ProcessedGames != 1) return;
            committedBeforeCancellation = db.Context.Games.AsNoTracking().Single(game => game.Id == db.Game.Id).SteamCompletionPreserved;
            cancel.Cancel();
        }, cancel.Token));
        Assert.True(committedBeforeCancellation);
        Assert.NotNull((await db.Context.Games.AsNoTracking().SingleAsync(game => game.Id == db.Game.Id)).SteamAchievementsSyncedAt);
        Assert.Null((await db.Context.Games.AsNoTracking().SingleAsync(game => game.Id == second.Id)).SteamAchievementsSyncedAt);
        Assert.Single(await db.Context.SteamAchievements.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Bulk_sync_handles_repeated_app_ids_without_duplicate_cache_inserts()
    {
        await using var db = await TestDatabase.CreateAsync();
        await PrepareBulkAsync(db, duplicateApp: true);
        var api = new SnapshotApi(SteamAchievementFixtures.Snapshot([1], [1], 0));
        var service = new SteamSyncService(db.Context, api, null!, NullLogger<SteamSyncService>.Instance, new(db.Context, api));
        var result = await service.SyncAllUserGamesAsync(db.Game.UserId);
        Assert.True(result.Success);
        Assert.Equal(2, result.ProcessedGames);
        Assert.Equal(0, result.FailedGames);
        Assert.Single(await db.Context.SteamAchievements.AsNoTracking().ToListAsync());
        Assert.Single(await db.Context.SteamAchievementSets.AsNoTracking().ToListAsync());
        Assert.All(await db.Context.Games.AsNoTracking().ToListAsync(), game => Assert.True(game.SteamCompletionPreserved));
    }

    [Fact]
    public async Task Bulk_sync_continues_after_one_failed_commit_without_erasing_other_games()
    {
        await using var db = await TestDatabase.CreateAsync();
        var second = await PrepareBulkAsync(db);
        second.Name = "Second failure";
        second.IsManuallyCompleted = true;
        var third = new Game { UserId = db.Game.UserId, StatusId = db.Game.StatusId, Name = "Third", SteamAppId = 622, Critic = 90, Logo = "https://images.test/logo.png", Hero = "https://images.test/hero.jpg", Cover = "https://images.test/cover.jpg" };
        db.Context.Games.Add(third);
        await db.Context.SaveChangesAsync();
        await db.Context.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_second_sync BEFORE UPDATE ON game WHEN OLD.name = 'Second failure' BEGIN SELECT RAISE(ABORT, 'PRIVATE_DB_ERROR'); END;");
        var api = new SnapshotApi(SteamAchievementFixtures.Snapshot([1], [1], 0));
        var service = new SteamSyncService(db.Context, api, null!, NullLogger<SteamSyncService>.Instance, new(db.Context, api));
        var result = await service.SyncAllUserGamesAsync(db.Game.UserId);
        Assert.True(result.Success);
        Assert.Equal(3, result.ProcessedGames);
        Assert.Equal(1, result.FailedGames);
        Assert.False(result.AchievementsSyncSucceeded);
        Assert.DoesNotContain("PRIVATE_DB_ERROR", string.Join(',', result.Warnings));
        Assert.Equal(2, await db.Context.SteamAchievements.CountAsync());
        Assert.True((await db.Context.Games.AsNoTracking().SingleAsync(game => game.Id == third.Id)).SteamCompletionPreserved);
        Assert.True((await db.Context.Games.AsNoTracking().SingleAsync(game => game.Id == second.Id)).IsManuallyCompleted);
        Assert.Null((await db.Context.Games.AsNoTracking().SingleAsync(game => game.Id == second.Id)).SteamAchievementsSyncedAt);
    }

    [Theory]
    [InlineData("noPlayerData")]
    [InlineData("unreleased")]
    public async Task Informational_skips_preserve_unlocks_sets_playtime_and_last_success(string notice)
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.SyncAsync(SteamAchievementFixtures.Snapshot([2], [2], 0));
        db.Game.SteamPlaytimeForever = 14351;
        await db.Context.SaveChangesAsync();
        var savedAt = db.Game.SteamAchievementsSyncedAt;
        var originalUnlock = (await db.Context.SteamAchievements.FirstAsync()).UnlockTime;
        var result = await db.SyncAsync(new() { SkipReason = notice });
        Assert.True(result.Success);
        Assert.Equal(notice, result.Outcome);
        Assert.Equal(0, result.Updated);
        Assert.Equal(savedAt, db.Game.SteamAchievementsSyncedAt);
        Assert.Equal(14351, db.Game.SteamPlaytimeForever);
        Assert.Equal(originalUnlock, (await db.Context.SteamAchievements.FirstAsync()).UnlockTime);
        Assert.All(await db.Context.SteamAchievements.ToListAsync(), achievement => Assert.True(achievement.Achieved));
        Assert.True((await db.Context.SteamAchievementSets.SingleAsync()).IsCompleted);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var profile = new SteamProfileService(db.Context, new SnapshotApi(new()), null!, cache, NullLogger<SteamProfileService>.Instance);
        var progress = await profile.GetAchievementProgressAsync(db.Game.UserId, db.Game.Id);
        Assert.Null(progress!.LastError);
        Assert.Equal(notice, progress.Notice);
        Assert.True(progress.IsCompleted);
    }

    [Fact]
    public async Task Progress_only_records_update_partial_progress_without_revoking_completed_sets()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.SyncAsync(SteamAchievementFixtures.Snapshot([1], [1], 0));
        var snapshot = SteamAchievementFixtures.Snapshot([2], [1]);
        snapshot.Schema!.Achievements![1].ProgressType = 1;
        snapshot.Schema.Achievements[1].MinProgressInt = 0;
        snapshot.Schema.Achievements[1].MaxProgressInt = 10;
        snapshot.Player!.Achievements!.Add(new() { InternalKey = 1, ProgressInt = 7 });
        await db.SyncAsync(snapshot);
        var partial = await db.Context.SteamAchievements.SingleAsync(achievement => achievement.ApiName == "ACH_1");
        Assert.False(partial.Achieved);
        Assert.Equal(7, partial.ProgressCurrent);
        Assert.True((await db.Context.SteamAchievementSets.SingleAsync()).IsCompleted);
        Assert.True(db.Game.SteamCompletionPreserved);
    }

    [Fact]
    public async Task Bulk_result_distinguishes_information_warnings_and_actual_errors()
    {
        await using var db = await TestDatabase.CreateAsync();
        await PrepareBulkAsync(db);
        foreach (var appId in new[] { 622, 623, 624 }) db.Context.Games.Add(new Game { UserId = db.Game.UserId, StatusId = db.Game.StatusId, Name = $"Game {appId}", SteamAppId = appId, Critic = 90, Logo = "https://images.test/logo.png", Hero = "https://images.test/hero.jpg", Cover = "https://images.test/cover.jpg" });
        await db.Context.SaveChangesAsync();
        var api = new OutcomesApi();
        var service = new SteamSyncService(db.Context, api, null!, NullLogger<SteamSyncService>.Instance, new(db.Context, api));
        var events = new List<SteamSyncProgress>();
        var result = await service.SyncAllUserGamesAsync(db.Game.UserId, events.Add);
        Assert.True(result.Success);
        Assert.Equal(5, result.ProcessedGames);
        Assert.Equal(3, result.InfoCount);
        Assert.Equal(1, result.WarningCount);
        Assert.Equal(1, result.FailedGames);
        Assert.Equal(5, result.ReasonCounts.Values.Sum());
        Assert.Equal(3, result.Issues.Count(issue => issue.Severity == "info"));
        Assert.Equal("warning", result.Issues.Single(issue => issue.Code == "profilePrivate").Severity);
        Assert.Equal("error", result.Issues.Single(issue => issue.Code == "invalidResponse").Severity);
        Assert.Equal(5, events.Count(progress => progress.Issue != null));
    }

    [Fact]
    public void Diagnostic_details_are_bounded_and_keep_actionable_errors_over_normal_skips()
    {
        var details = Enumerable.Range(1, SteamSyncClassification.DetailLimit).Select(id => new SteamSyncIssueDto(id, id, "Game", "info", "noPlayerData")).ToList();
        SteamSyncClassification.Retain(details, new(9999, 9999, "Error game", "error", "invalidResponse"));
        SteamSyncClassification.Retain(details, new(9998, 9998, "Warning game", "warning", "profilePrivate"));
        Assert.Equal(SteamSyncClassification.DetailLimit, details.Count);
        Assert.Contains(details, detail => detail.GameId == 9999);
        Assert.Contains(details, detail => detail.GameId == 9998);
    }

    private sealed class OutcomesApi() : SnapshotApi(new())
    {
        public override Task<SteamAchievementSnapshot> GetAchievementSnapshotAsync(string steamId, int appId) => Task.FromResult(appId switch
        {
            620 => new SteamAchievementSnapshot { SkipReason = "noPlayerData" },
            621 => new SteamAchievementSnapshot { SkipReason = "unreleased" },
            622 => new SteamAchievementSnapshot { Success = true, SupportsSets = true, NoAchievements = true },
            623 => new SteamAchievementSnapshot { Error = "invalidResponse" },
            _ => new SteamAchievementSnapshot { Error = "profilePrivate" }
        });
    }

    private static async Task<Game> PrepareBulkAsync(TestDatabase db, bool duplicateApp = false)
    {
        (await db.Context.Users.SingleAsync()).SteamId = "76561198000000000";
        db.Game.Critic = 90;
        db.Game.Logo = "https://images.test/logo.png";
        db.Game.Hero = "https://images.test/hero.jpg";
        db.Game.Cover = "https://images.test/cover.jpg";
        var second = new Game { UserId = db.Game.UserId, StatusId = db.Game.StatusId, Name = "Second", SteamAppId = duplicateApp ? 620 : 621, Critic = 90, Logo = "https://images.test/logo.png", Hero = "https://images.test/hero.jpg", Cover = "https://images.test/cover.jpg" };
        db.Context.Games.Add(second);
        await db.Context.SaveChangesAsync();
        return second;
    }

    [Fact]
    public async Task Bulk_sync_stops_if_the_link_changes_before_commit_and_preserves_previous_completion()
    {
        await using var db = await TestDatabase.CreateAsync();
        await PrepareBulkAsync(db);
        db.Game.SteamCompletionPreserved = true;
        await db.Context.SaveChangesAsync();
        var options = new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(db.Context.Database.GetDbConnection()).Options;
        var api = new CallbackApi(SteamAchievementFixtures.Snapshot([1], [1], 0), async () =>
        {
            await using var otherRequest = new GamesDbContext(options);
            (await otherRequest.Users.SingleAsync()).SteamId = "76561198000000009";
            await otherRequest.SaveChangesAsync();
        });
        var service = new SteamSyncService(db.Context, api, null!, NullLogger<SteamSyncService>.Instance, new(db.Context, api));
        var result = await service.SyncAllUserGamesAsync(db.Game.UserId);
        Assert.False(result.Success);
        Assert.Equal("steamAccountChanged", result.Error);
        Assert.Equal(0, result.ProcessedGames);
        Assert.Empty(await db.Context.SteamAchievements.ToListAsync());
        Assert.True((await db.Context.Games.AsNoTracking().SingleAsync(game => game.Id == db.Game.Id)).SteamCompletionPreserved);
    }

    private class SnapshotApi(SteamAchievementSnapshot snapshot) : ISteamApiService
    {
        public virtual Task<SteamAchievementSnapshot> GetAchievementSnapshotAsync(string steamId, int appId) => Task.FromResult(snapshot);
        public virtual Task<List<SteamOwnedGameDto>> GetOwnedGamesAsync(string steamId) => Task.FromResult(new List<SteamOwnedGameDto>());
        public Task<SteamPlayerAchievementsResult> GetPlayerAchievementsAsync(string steamId, int appId) => throw new NotSupportedException();
        public Task<SteamGameSchemaDto?> GetGameSchemaAsync(int appId) => throw new NotSupportedException();
        public Task<SteamPlayerSummaryDto?> GetPlayerSummaryAsync(string steamId) => Task.FromResult<SteamPlayerSummaryDto?>(null);
    }

    private sealed class CallbackApi(SteamAchievementSnapshot snapshot, Func<Task> onSnapshot) : SnapshotApi(snapshot)
    {
        public override async Task<SteamAchievementSnapshot> GetAchievementSnapshotAsync(string steamId, int appId)
        {
            await onSnapshot();
            return await base.GetAchievementSnapshotAsync(steamId, appId);
        }
    }

    private sealed class BlockingApi(SteamAchievementSnapshot snapshot) : SnapshotApi(snapshot)
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int OwnedCalls;
        public override async Task<List<SteamOwnedGameDto>> GetOwnedGamesAsync(string steamId)
        {
            Interlocked.Increment(ref OwnedCalls);
            await Release.Task;
            return [];
        }
    }

    private sealed class TestDatabase(SqliteConnection connection, GamesDbContext context, Game game) : IAsyncDisposable
    {
        public GamesDbContext Context { get; } = context;
        public Game Game { get; } = game;
        public SteamAchievement Achievement(string name, bool achieved) => new() { UserId = Game.UserId, GameId = Game.Id, SteamAppId = Game.SteamAppId!.Value, ApiName = name, DisplayName = name, Achieved = achieved, UnlockTime = achieved ? DateTime.UtcNow.AddDays(-1) : null };
        public async Task<SteamAchievementSyncResult> SyncAsync(SteamAchievementSnapshot snapshot)
        {
            var result = await new SteamAchievementSyncService(Context, new SnapshotApi(snapshot)).SyncAsync("76561198000000000", Game);
            await Context.SaveChangesAsync();
            return result;
        }
        public static async Task<TestDatabase> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var context = new GamesDbContext(new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(connection).Options);
            await context.Database.EnsureCreatedAsync();
            var user = new User { Username = "Steam test", Role = UserRole.Standard };
            context.Users.Add(user);
            await context.SaveChangesAsync();
            var status = new GameStatus { UserId = user.Id, Name = "Backlog" };
            context.GameStatuses.Add(status);
            await context.SaveChangesAsync();
            var game = new Game { UserId = user.Id, StatusId = status.Id, Name = "Game", SteamAppId = 620 };
            context.Games.Add(game);
            await context.SaveChangesAsync();
            return new(connection, context, game);
        }
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
