using System.Text.Json;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class MetadataAutofillMigrationTests
{
    [Fact]
    public async Task Generated_up_down_up_only_adds_job_storage_and_preserves_games_preferences_ids_sequences_and_foreign_keys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new GamesDbContext(new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(connection).Options);
        await DatabaseInitialization.InitializeAsync(db);
        Assert.False(db.Database.HasPendingModelChanges());
        var user = new User { Username = "Job migration fixture", ScoreProvider = "OpenCritic" }; db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Fixture backlog" }; db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var game = new Game { UserId = user.Id, StatusId = status.Id, Name = "Retained fixture", Story = 0, Completion = 90, Grade = 0, Critic = 88, CriticProvider = "Metacritic", Comment = "Retain fixture note", Favorite = true }; db.Games.Add(game); await db.SaveChangesAsync();
        var deleted = new Game { UserId = user.Id, StatusId = status.Id, Name = "Deleted highest ID" }; db.Games.Add(deleted); await db.SaveChangesAsync(); var highest = deleted.Id; db.Games.Remove(deleted); await db.SaveChangesAsync();
        async Task<string> Snapshot()
        {
            await using var command = connection.CreateCommand(); command.CommandText = "SELECT id,user_id,name,status_id,story,completion,grade,critic,critic_provider,comment,favorite,created_at,updated_at,modified_since_export FROM game ORDER BY id";
            await using var reader = await command.ExecuteReaderAsync(); var rows = new List<object[]>(); while (await reader.ReadAsync()) { var values = new object[reader.FieldCount]; reader.GetValues(values); rows.Add(values); } return JsonSerializer.Serialize(rows);
        }
        var original = await Snapshot();
        var migrator = db.GetService<IMigrator>();
        for (var pass = 0; pass < 2; pass++)
        {
            await migrator.MigrateAsync("20261126174632_AddPinnedQuickViews"); Assert.Equal(original, await Snapshot());
            await migrator.MigrateAsync(); db.ChangeTracker.Clear(); Assert.Equal(original, await Snapshot());
            Assert.Empty(await db.MetadataAutofillJobs.ToListAsync()); Assert.Equal("OpenCritic", (await db.Users.SingleAsync(item => item.Id == user.Id)).ScoreProvider);
        }
        var next = new Game { UserId = user.Id, StatusId = status.Id, Name = "After additive job migration" }; db.Games.Add(next); await db.SaveChangesAsync(); Assert.True(next.Id > highest);
        await using var check = connection.CreateCommand(); check.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", await check.ExecuteScalarAsync()); check.CommandText = "PRAGMA foreign_key_check"; await using var errors = await check.ExecuteReaderAsync(); Assert.False(await errors.ReadAsync());
    }
}
