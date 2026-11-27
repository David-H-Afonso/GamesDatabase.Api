using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GamesDatabase.Api.Tests;

public sealed class PinnedViewMigrationTests
{
    [Fact]
    public async Task Up_down_up_preserves_view_rows_foreign_keys_and_deleted_highest_ID_sequence()
    {
        var path = Path.Combine(Path.GetTempPath(), "gdb-pin-migration-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await using var connection = new SqliteConnection($"Data Source={path}"); await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<GamesDbContext>().UseSqlite(connection).Options; await using var db = new GamesDbContext(options);
            await DatabaseInitialization.InitializeAsync(db);
            var user = new User { Username = "Migration fixture" }; db.Users.Add(user); await db.SaveChangesAsync();
            var views = Enumerable.Range(1, 3).Select(index => new GameView { UserId = user.Id, Name = "Migration view " + index, FiltersJson = "[]" }).ToArray(); db.GameViews.AddRange(views); await db.SaveChangesAsync(); var high = views.Max(view => view.Id); db.GameViews.Remove(views[^1]); await db.SaveChangesAsync();
            var migrator = db.GetService<IMigrator>(); await migrator.MigrateAsync("20261126091145_AddRetrySafeGameCreation");
            await using (var command = connection.CreateCommand()) { command.CommandText = "SELECT seq FROM sqlite_sequence WHERE name='game_view'"; Assert.Equal((long)high, (long)(await command.ExecuteScalarAsync())!); command.CommandText = "SELECT COUNT(*) FROM game_view"; Assert.Equal(2L, (long)(await command.ExecuteScalarAsync())!); }
            await migrator.MigrateAsync(); db.ChangeTracker.Clear();
            Assert.Equal(2, await db.GameViews.CountAsync()); Assert.All(await db.GameViews.AsNoTracking().ToListAsync(), view => Assert.Null(view.PinnedSlot));
            var next = new GameView { UserId = user.Id, Name = "After migration", FiltersJson = "[]" }; db.GameViews.Add(next); await db.SaveChangesAsync(); Assert.True(next.Id > high);
            await using var check = connection.CreateCommand(); check.CommandText = "PRAGMA integrity_check"; Assert.Equal("ok", await check.ExecuteScalarAsync()); check.CommandText = "PRAGMA foreign_key_check"; await using var errors = await check.ExecuteReaderAsync(); Assert.False(await errors.ReadAsync());
        }
        finally { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { var file = path + suffix; if (File.Exists(file)) File.Delete(file); } }
    }
}
