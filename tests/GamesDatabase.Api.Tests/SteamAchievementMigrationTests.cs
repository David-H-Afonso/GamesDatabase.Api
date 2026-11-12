using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class SteamAchievementMigrationTests
{
    [Fact]
    public async Task Generated_migration_preserves_exact_legacy_completion_without_touching_manual_flags_or_unlocks()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new GamesDbContext(new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(connection).Options);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "user" ("id" INTEGER PRIMARY KEY);
            CREATE TABLE "game" ("id" INTEGER PRIMARY KEY, "user_id" INTEGER NOT NULL, "IsManuallyCompleted" INTEGER NOT NULL DEFAULT 0, "completion" INTEGER NULL);
            CREATE TABLE "steam_achievement" ("id" INTEGER PRIMARY KEY, "user_id" INTEGER NOT NULL, "game_id" INTEGER, "api_name" TEXT, "achieved" INTEGER NOT NULL);
            INSERT INTO "user" VALUES (1), (2);
            INSERT INTO "game" VALUES (1,1,0,NULL), (2,1,0,NULL), (3,1,1,NULL), (4,1,0,100), (5,2,0,NULL), (6,1,0,NULL);
            INSERT INTO "steam_achievement" VALUES (1,1,1,'A',1), (2,1,1,'B',1), (3,1,2,'A',1), (4,1,2,'B',0), (5,2,5,'A',1), (6,2,6,'FOREIGN',1);
            """);

        var migration = new AddSteamAchievementSets();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(migration.UpOperations, migration.TargetModel))
            await context.Database.ExecuteSqlRawAsync(command.CommandText);

        using var read = connection.CreateCommand();
        read.CommandText = "SELECT id, steam_completion_preserved, IsManuallyCompleted FROM game ORDER BY id";
        using (var rows = await read.ExecuteReaderAsync())
        {
            var preserved = new Dictionary<long, bool>();
            while (await rows.ReadAsync())
            {
                preserved[rows.GetInt64(0)] = rows.GetBoolean(1);
                if (rows.GetInt64(0) == 3) Assert.True(rows.GetBoolean(2));
            }
            Assert.Equal(new long[] { 1, 5 }, preserved.Where(item => item.Value).Select(item => item.Key));
        }
        read.CommandText = "SELECT COUNT(*) FROM steam_achievement WHERE achieved = 1";
        Assert.Equal(5L, (long)(await read.ExecuteScalarAsync())!);
        read.CommandText = "SELECT COUNT(*) FROM steam_achievement_set";
        Assert.Equal(0L, (long)(await read.ExecuteScalarAsync())!);
    }
}
