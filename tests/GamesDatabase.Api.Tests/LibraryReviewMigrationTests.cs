using System.Text.Json;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class LibraryReviewMigrationTests
{
    [Fact]
    public async Task Generated_review_migrations_up_down_up_preserve_prior_game_values_ids_and_foreign_keys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new GamesDbContext(new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var owner = new User { Username = "Migration fixture", Role = UserRole.Standard }; context.Users.Add(owner); await context.SaveChangesAsync();
        var status = new GameStatus { UserId = owner.Id, Name = "Fixture backlog" }; context.GameStatuses.Add(status); await context.SaveChangesAsync();
        var game = new Game { UserId = owner.Id, StatusId = status.Id, Name = "Legacy manual zero", ManualPlaytimeMinutes = 0, SteamPlaytimeForever = 600, Grade = 0, Critic = 90, Story = 10, Finished = "2026-03-01" }; game.CalculateScore(); context.Games.Add(game); await context.SaveChangesAsync();
        var deleted = new Game { UserId = owner.Id, StatusId = status.Id, Name = "Deleted sequence fixture" }; context.Games.Add(deleted); await context.SaveChangesAsync(); context.Games.Remove(deleted); await context.SaveChangesAsync();
        var view = new GameView { UserId = owner.Id, Name = "Stored view", FiltersJson = "[]", SortOrder = 7, ModifiedSinceExport = false }; context.GameViews.Add(view); await context.SaveChangesAsync();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var reviews = new AddLibraryReviewPreferences(); var receipts = new AddRetrySafeGameCreation(); var beforeReviews = new AddOnboardingAndSamples();
        async Task Apply(IEnumerable<Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation> operations, Microsoft.EntityFrameworkCore.Metadata.IModel model)
        {
            var initialized = context.GetService<IModelRuntimeInitializer>().Initialize(model, designTime: true);
            await using var transaction = await context.Database.BeginTransactionAsync();
            foreach (var command in generator.Generate(operations.ToList(), initialized)) await context.Database.ExecuteSqlRawAsync(command.CommandText);
            await transaction.CommitAsync();
        }
        async Task<string> Snapshot()
        {
            using var command = connection.CreateCommand(); command.CommandText = "SELECT id,user_id,name,status_id,manual_playtime_minutes,steam_playtime_forever,grade,critic,story,score,finished,created_at,updated_at,modified_since_export FROM game ORDER BY id";
            using var reader = await command.ExecuteReaderAsync(); var rows = new List<object[]>(); while (await reader.ReadAsync()) { var values = new object[reader.FieldCount]; reader.GetValues(values); rows.Add(values); }
            return JsonSerializer.Serialize(rows);
        }
        var original = await Snapshot();
        using var scalar = connection.CreateCommand(); scalar.CommandText = "SELECT seq FROM sqlite_sequence WHERE name='game'"; var sequence = await scalar.ExecuteScalarAsync();
        await Apply(receipts.DownOperations, reviews.TargetModel); await Apply(reviews.DownOperations, beforeReviews.TargetModel);
        Assert.Equal(original, await Snapshot());
        for (var pass = 0; pass < 2; pass++)
        {
            await Apply(reviews.UpOperations, reviews.TargetModel); await Apply(receipts.UpOperations, receipts.TargetModel);
            Assert.Equal(original, await Snapshot());
            scalar.CommandText = "SELECT manual_playtime_is_additive FROM game"; Assert.Equal(0L, await scalar.ExecuteScalarAsync());
            scalar.CommandText = "SELECT last_used_at FROM game_view"; Assert.Equal(DBNull.Value, await scalar.ExecuteScalarAsync());
            scalar.CommandText = "SELECT COUNT(*) FROM game WHERE creation_request_id IS NOT NULL OR creation_request_hash IS NOT NULL"; Assert.Equal(0L, await scalar.ExecuteScalarAsync());
            if (pass == 0) { await Apply(receipts.DownOperations, reviews.TargetModel); await Apply(reviews.DownOperations, beforeReviews.TargetModel); Assert.Equal(original, await Snapshot()); }
        }
        scalar.CommandText = "SELECT seq FROM sqlite_sequence WHERE name='game'"; Assert.Equal(sequence, await scalar.ExecuteScalarAsync());
        scalar.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", await scalar.ExecuteScalarAsync());
        scalar.CommandText = "PRAGMA foreign_key_check"; using var foreignKeys = await scalar.ExecuteReaderAsync(); Assert.False(await foreignKeys.ReadAsync());
    }
}
