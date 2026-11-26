using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class CollectionExperienceMigrationTests
{
    [Fact]
    public async Task Generated_up_and_transactional_down_preserve_existing_values_and_ids()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new GamesDbContext(new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(connection).Options);
        await context.Database.ExecuteSqlRawAsync("""
            PRAGMA foreign_keys=ON;
            CREATE TABLE "user" ("id" INTEGER PRIMARY KEY);
            CREATE TABLE "game" ("id" INTEGER PRIMARY KEY AUTOINCREMENT,"user_id" INTEGER NOT NULL,"name" TEXT NOT NULL,"finished" TEXT,"manual_playtime_minutes" INTEGER,"grade" INTEGER,"score" TEXT);
            INSERT INTO "user" VALUES(1);
            INSERT INTO "game" VALUES(17,1,'Original game','2026-03-01',0,0,'0.48');
            INSERT INTO "game" VALUES(30,1,'Deleted last ID',NULL,NULL,NULL,NULL);
            DELETE FROM "game" WHERE id=30;
            """);
        var migration = new AddCollectionExperience(); var generator = context.GetService<IMigrationsSqlGenerator>();
        foreach (var command in generator.Generate(migration.UpOperations, migration.TargetModel)) await context.Database.ExecuteSqlRawAsync(command.CommandText);
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO collection_preferences(user_id,home_config_json,goty_selections_json,revision,updated_at) VALUES(1,{"{}"},{"{}"},1,'2026-11-24');");
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            foreach (var command in generator.Generate(migration.DownOperations, migration.TargetModel)) await context.Database.ExecuteSqlRawAsync(command.CommandText);
            await transaction.CommitAsync();
        }
        using var read = connection.CreateCommand(); read.CommandText = "SELECT id, name, finished, manual_playtime_minutes, grade, score FROM game";
        using (var rows = await read.ExecuteReaderAsync())
        {
            Assert.True(await rows.ReadAsync()); Assert.Equal(17L, rows.GetInt64(0)); Assert.Equal("Original game", rows.GetString(1)); Assert.Equal("2026-03-01", rows.GetString(2)); Assert.Equal(0L, rows.GetInt64(3)); Assert.Equal(0L, rows.GetInt64(4)); Assert.Equal("0.48", rows.GetString(5)); Assert.False(await rows.ReadAsync());
        }
        read.CommandText = "SELECT seq FROM sqlite_sequence WHERE name='game'"; Assert.Equal(30L, (long)(await read.ExecuteScalarAsync())!);
        read.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE name='collection_preferences'"; Assert.Equal(0L, (long)(await read.ExecuteScalarAsync())!);
        read.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", await read.ExecuteScalarAsync());
    }
}
