using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class ArtworkStartupCompatibilityTests
{
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar();
    }

    private static SqliteConnection Schema(bool modern)
    {
        var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        Execute(connection, """
            CREATE TABLE "user" (id INTEGER PRIMARY KEY);
            CREATE TABLE game (id INTEGER PRIMARY KEY, user_id INTEGER NOT NULL, cover TEXT NULL);
            CREATE TABLE game_replay (id INTEGER PRIMARY KEY);
            CREATE TABLE game_export_cache (id INTEGER PRIMARY KEY, cover_url TEXT NULL, cover_downloaded INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE playlist (id INTEGER PRIMARY KEY);
            INSERT INTO "user" (id) VALUES (1);
            INSERT INTO game (id, user_id, cover) VALUES (1, 1, 'stored-cover');
            INSERT INTO game_export_cache (id, cover_url, cover_downloaded) VALUES (1, 'stored-cover', 1);
            """);
        if (modern) Execute(connection, "ALTER TABLE game ADD COLUMN hero TEXT NULL; ALTER TABLE game_export_cache ADD COLUMN hero_url TEXT NULL; ALTER TABLE game_export_cache ADD COLUMN hero_downloaded INTEGER NOT NULL DEFAULT 0;");
        return connection;
    }

    [Fact]
    public void Modern_startup_does_not_turn_vertical_Cover_into_Hero_or_fake_a_download()
    {
        using var connection = Schema(true);
        using var db = new GamesDbContext(new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(connection).Options);
        new DatabaseStartupHelper().EnsureCompatibilitySchema(db, NullLogger.Instance);
        Assert.Equal(DBNull.Value, Scalar(connection, "SELECT hero FROM game WHERE id=1"));
        Assert.Equal(DBNull.Value, Scalar(connection, "SELECT hero_url FROM game_export_cache WHERE id=1"));
        Assert.Equal(0L, Scalar(connection, "SELECT hero_downloaded FROM game_export_cache WHERE id=1"));
        Assert.Equal("stored-cover", Scalar(connection, "SELECT cover FROM game WHERE id=1"));
    }

    [Fact]
    public void A_truly_legacy_schema_is_backfilled_once_but_not_again_after_Hero_is_cleared()
    {
        using var connection = Schema(false);
        using var db = new GamesDbContext(new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(connection).Options);
        var helper = new DatabaseStartupHelper(); helper.EnsureCompatibilitySchema(db, NullLogger.Instance);
        Assert.Equal("stored-cover", Scalar(connection, "SELECT hero FROM game WHERE id=1"));
        Assert.Equal("stored-cover", Scalar(connection, "SELECT hero_url FROM game_export_cache WHERE id=1"));
        Assert.Equal(1L, Scalar(connection, "SELECT hero_downloaded FROM game_export_cache WHERE id=1"));
        Execute(connection, "UPDATE game SET hero=NULL, cover='new-vertical-cover'; UPDATE game_export_cache SET hero_url=NULL, hero_downloaded=0, cover_url='new-vertical-cover';");
        helper.EnsureCompatibilitySchema(db, NullLogger.Instance);
        Assert.Equal(DBNull.Value, Scalar(connection, "SELECT hero FROM game WHERE id=1"));
        Assert.Equal(DBNull.Value, Scalar(connection, "SELECT hero_url FROM game_export_cache WHERE id=1"));
        Assert.Equal(0L, Scalar(connection, "SELECT hero_downloaded FROM game_export_cache WHERE id=1"));
    }
}
