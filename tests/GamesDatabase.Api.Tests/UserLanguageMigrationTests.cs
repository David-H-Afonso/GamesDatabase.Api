using System.Text.Json;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class UserLanguageMigrationTests
{
    [Fact]
    public async Task Generated_language_migration_only_adds_a_nullable_preference_and_preserves_every_existing_table_and_sequence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = new GamesDbContext(new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(connection).Options);
        await DatabaseInitialization.InitializeAsync(db); Assert.False(db.Database.HasPendingModelChanges());
        var user = new User { Username = "Language migration fixture", Role = UserRole.Admin, UseScoreColors = true, ScoreProvider = "OpenCritic", TutorialEnabled = true, SteamId = "76561198000000000" };
        db.Users.Add(user); await db.SaveChangesAsync();
        var status = new GameStatus { UserId = user.Id, Name = "Fixture backlog" }; db.GameStatuses.Add(status); await db.SaveChangesAsync();
        var game = new Game { UserId = user.Id, StatusId = status.Id, Name = "Retained fixture", Grade = 0, Story = 0, Completion = 90, Critic = 88, CriticProvider = "Metacritic", Comment = "Keep fixture notes", Favorite = true };
        db.Games.Add(game); await db.SaveChangesAsync();
        var deleted = new Game { UserId = user.Id, StatusId = status.Id, Name = "Deleted highest ID" }; db.Games.Add(deleted); await db.SaveChangesAsync(); var highest = deleted.Id; db.Games.Remove(deleted); await db.SaveChangesAsync();
        db.MetadataAutofillJobs.Add(new MetadataAutofillJob { UserId = user.Id, ClientRequestId = Guid.NewGuid(), RequestHash = new string('a', 64), State = "completed", ItemsJson = "[]" }); await db.SaveChangesAsync();

        async Task<string> Snapshot()
        {
            var tables = new List<string>();
            await using (var names = connection.CreateCommand())
            {
                names.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT IN ('__EFMigrationsHistory','__EFMigrationsLock') ORDER BY name";
                await using var reader = await names.ExecuteReaderAsync(); while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
            }
            var snapshot = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var table in tables)
            {
                await using var rows = connection.CreateCommand(); rows.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\"";
                await using var reader = await rows.ExecuteReaderAsync(); var values = new List<string>();
                while (await reader.ReadAsync())
                {
                    var fields = new SortedDictionary<string, object?>(StringComparer.Ordinal);
                    for (var index = 0; index < reader.FieldCount; index++)
                        if (table != "user" || reader.GetName(index) != "preferred_language") fields[reader.GetName(index)] = reader.IsDBNull(index) ? null : reader.GetValue(index);
                    values.Add(JsonSerializer.Serialize(fields));
                }
                values.Sort(StringComparer.Ordinal); snapshot[table] = JsonSerializer.Serialize(values);
            }
            return JsonSerializer.Serialize(snapshot);
        }

        var before = await Snapshot(); var migrator = db.GetService<IMigrator>();
        for (var pass = 0; pass < 2; pass++)
        {
            await migrator.MigrateAsync("20261127115001_AddMetadataAutofillJobs"); Assert.Equal(before, await Snapshot());
            await migrator.MigrateAsync(); db.ChangeTracker.Clear(); Assert.Equal(before, await Snapshot());
            Assert.Null((await db.Users.SingleAsync()).PreferredLanguage);
        }
        var next = new Game { UserId = user.Id, StatusId = status.Id, Name = "After language migration" }; db.Games.Add(next); await db.SaveChangesAsync(); Assert.True(next.Id > highest);
        await using var check = connection.CreateCommand(); check.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", await check.ExecuteScalarAsync());
        check.CommandText = "PRAGMA foreign_key_check"; await using var errors = await check.ExecuteReaderAsync(); Assert.False(await errors.ReadAsync());
    }
}
