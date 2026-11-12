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

    private class SnapshotApi(SteamAchievementSnapshot snapshot) : ISteamApiService
    {
        public Task<SteamAchievementSnapshot> GetAchievementSnapshotAsync(string steamId, int appId) => Task.FromResult(snapshot);
        public virtual Task<List<SteamOwnedGameDto>> GetOwnedGamesAsync(string steamId) => Task.FromResult(new List<SteamOwnedGameDto>());
        public Task<SteamPlayerAchievementsResult> GetPlayerAchievementsAsync(string steamId, int appId) => throw new NotSupportedException();
        public Task<SteamGameSchemaDto?> GetGameSchemaAsync(int appId) => throw new NotSupportedException();
        public Task<SteamPlayerSummaryDto?> GetPlayerSummaryAsync(string steamId) => Task.FromResult<SteamPlayerSummaryDto?>(null);
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
