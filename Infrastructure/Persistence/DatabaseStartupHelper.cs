using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Infrastructure.Persistence;

public class DatabaseStartupHelper
{
    public void EnsureCompatibilitySchema(GamesDbContext context, ILogger logger)
    {
        var conn = (SqliteConnection)context.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
            conn.Open();

        EnsureColumn(conn, logger, "game_replay", "released", "TEXT NULL");

        EnsureColumn(conn, logger, "user", "steam_avatar_url", "TEXT NULL");
        EnsureColumn(conn, logger, "user", "steam_id", "TEXT NULL");
        EnsureColumn(conn, logger, "user", "steam_linked_at", "TEXT NULL");
        EnsureColumn(conn, logger, "user", "steam_nickname", "TEXT NULL");

        EnsureColumn(conn, logger, "game", "steam_app_id", "INTEGER NULL");
        EnsureColumn(conn, logger, "game", "steam_last_synced", "TEXT NULL");
        EnsureColumn(conn, logger, "game", "steam_playtime_2weeks", "INTEGER NULL");
        EnsureColumn(conn, logger, "game", "steam_playtime_forever", "INTEGER NULL");
        EnsureColumn(conn, logger, "game", "steam_finished_source", "TEXT NULL");
        EnsureColumn(conn, logger, "game", "steam_finished_last_value", "TEXT NULL");
        EnsureColumn(conn, logger, "game", "steam_finished_synced_at", "TEXT NULL");
        EnsureColumn(conn, logger, "game", "steam_finished_rejected_value", "TEXT NULL");
        EnsureColumn(conn, logger, "game", "steam_started_rejected_value", "TEXT NULL");
        EnsureColumn(conn, logger, "game", "IsManuallyCompleted", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, logger, "game", "hero", "TEXT NULL");
        EnsureColumn(conn, logger, "game_export_cache", "hero_url", "TEXT NULL");
        EnsureColumn(conn, logger, "game_export_cache", "hero_downloaded", "INTEGER NOT NULL DEFAULT 0");

        ExecuteRepairSql(conn, logger,
            "UPDATE game SET hero = cover WHERE hero IS NULL AND cover IS NOT NULL;",
            "migrated legacy game.cover values into game.hero when missing");
        ExecuteRepairSql(conn, logger,
            "UPDATE game_export_cache SET hero_url = cover_url WHERE hero_url IS NULL AND cover_url IS NOT NULL;",
            "migrated legacy export-cache cover_url values into hero_url when missing");
        ExecuteRepairSql(conn, logger,
            "UPDATE game_export_cache SET hero_downloaded = cover_downloaded WHERE hero_downloaded = 0 AND cover_downloaded = 1;",
            "migrated legacy export-cache cover_downloaded values into hero_downloaded when missing");

        ExecuteRepairSql(conn, logger, """
            CREATE TABLE IF NOT EXISTS "steam_achievement" (
                "id" INTEGER NOT NULL CONSTRAINT "PK_steam_achievement" PRIMARY KEY AUTOINCREMENT,
                "user_id" INTEGER NOT NULL,
                "game_id" INTEGER NULL,
                "steam_app_id" INTEGER NOT NULL,
                "api_name" TEXT NOT NULL,
                "display_name" TEXT NOT NULL,
                "description" TEXT NULL,
                "achieved" INTEGER NOT NULL DEFAULT 0,
                "unlock_time" TEXT NULL,
                "icon_url" TEXT NULL,
                "icon_gray_url" TEXT NULL,
                "hidden" INTEGER NOT NULL DEFAULT 0,
                "last_synced" TEXT NOT NULL,
                CONSTRAINT "FK_steam_achievement_game_game_id" FOREIGN KEY ("game_id") REFERENCES "game" ("id") ON DELETE SET NULL,
                CONSTRAINT "FK_steam_achievement_user_user_id" FOREIGN KEY ("user_id") REFERENCES "user" ("id") ON DELETE CASCADE
            );
            """, "ensured steam_achievement table exists");

        ExecuteRepairSql(conn, logger, """
            CREATE TABLE IF NOT EXISTS "steam_app_cache" (
                "app_id" INTEGER NOT NULL CONSTRAINT "PK_steam_app_cache" PRIMARY KEY AUTOINCREMENT,
                "name" TEXT NOT NULL,
                "developer" TEXT NULL,
                "publisher" TEXT NULL,
                "genres_json" TEXT NULL,
                "categories_json" TEXT NULL,
                "release_date" TEXT NULL,
                "metacritic_score" INTEGER NULL,
                "header_image_url" TEXT NULL,
                "background_image_url" TEXT NULL,
                "price" TEXT NULL,
                "is_free" INTEGER NOT NULL DEFAULT 0,
                "last_fetched" TEXT NOT NULL
            );
            """, "ensured steam_app_cache table exists");

        ExecuteRepairSql(conn, logger, """
            CREATE TABLE IF NOT EXISTS "steam_match_dismissal" (
                "id" INTEGER NOT NULL CONSTRAINT "PK_steam_match_dismissal" PRIMARY KEY AUTOINCREMENT,
                "user_id" INTEGER NOT NULL,
                "steam_app_id" INTEGER NOT NULL,
                "game_id" INTEGER NOT NULL,
                "created_at" TEXT NOT NULL,
                CONSTRAINT "FK_steam_match_dismissal_game_game_id" FOREIGN KEY ("game_id") REFERENCES "game" ("id") ON DELETE CASCADE,
                CONSTRAINT "FK_steam_match_dismissal_user_user_id" FOREIGN KEY ("user_id") REFERENCES "user" ("id") ON DELETE CASCADE
            );
            """, "ensured steam_match_dismissal table exists");

        ExecuteRepairSql(conn, logger,
            "CREATE INDEX IF NOT EXISTS \"IX_steam_achievement_game_id\" ON \"steam_achievement\" (\"game_id\");",
            "ensured steam achievement game index exists");
        ExecuteRepairSql(conn, logger,
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_steam_achievement_user_id_steam_app_id_api_name\" ON \"steam_achievement\" (\"user_id\", \"steam_app_id\", \"api_name\");",
            "ensured steam achievement unique index exists");
        ExecuteRepairSql(conn, logger,
            "CREATE INDEX IF NOT EXISTS \"IX_steam_match_dismissal_game_id\" ON \"steam_match_dismissal\" (\"game_id\");",
            "ensured steam match dismissal game index exists");
        ExecuteRepairSql(conn, logger,
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_steam_match_dismissal_user_id_steam_app_id_game_id\" ON \"steam_match_dismissal\" (\"user_id\", \"steam_app_id\", \"game_id\");",
            "ensured steam match dismissal unique index exists");

        ExecuteRepairSql(conn, logger, """
            CREATE TABLE IF NOT EXISTS "duplicate_game_dismissal" (
                "id" INTEGER NOT NULL CONSTRAINT "PK_duplicate_game_dismissal" PRIMARY KEY AUTOINCREMENT,
                "user_id" INTEGER NOT NULL,
                "game_id_a" INTEGER NOT NULL,
                "game_id_b" INTEGER NOT NULL,
                "created_at" TEXT NOT NULL,
                CONSTRAINT "FK_duplicate_game_dismissal_game_game_id_a" FOREIGN KEY ("game_id_a") REFERENCES "game" ("id") ON DELETE CASCADE,
                CONSTRAINT "FK_duplicate_game_dismissal_game_game_id_b" FOREIGN KEY ("game_id_b") REFERENCES "game" ("id") ON DELETE CASCADE,
                CONSTRAINT "FK_duplicate_game_dismissal_user_user_id" FOREIGN KEY ("user_id") REFERENCES "user" ("id") ON DELETE CASCADE
            );
            """, "ensured duplicate_game_dismissal table exists");
        ExecuteRepairSql(conn, logger,
            "CREATE INDEX IF NOT EXISTS \"IX_duplicate_game_dismissal_game_id_a\" ON \"duplicate_game_dismissal\" (\"game_id_a\");",
            "ensured duplicate game dismissal game_id_a index exists");
        ExecuteRepairSql(conn, logger,
            "CREATE INDEX IF NOT EXISTS \"IX_duplicate_game_dismissal_game_id_b\" ON \"duplicate_game_dismissal\" (\"game_id_b\");",
            "ensured duplicate game dismissal game_id_b index exists");
        ExecuteRepairSql(conn, logger,
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_duplicate_game_dismissal_user_id_game_id_a_game_id_b\" ON \"duplicate_game_dismissal\" (\"user_id\", \"game_id_a\", \"game_id_b\");",
            "ensured duplicate game dismissal unique index exists");

        // Refresh tokens table (JWT security)
        ExecuteRepairSql(conn, logger, """
            CREATE TABLE IF NOT EXISTS "refresh_token" (
                "id" INTEGER NOT NULL CONSTRAINT "PK_refresh_token" PRIMARY KEY AUTOINCREMENT,
                "user_id" INTEGER NOT NULL,
                "token" TEXT NOT NULL,
                "expires_at" TEXT NOT NULL,
                "created_at" TEXT NOT NULL,
                "revoked" INTEGER NOT NULL DEFAULT 0,
                "revoked_at" TEXT NULL,
                CONSTRAINT "FK_refresh_token_user_user_id" FOREIGN KEY ("user_id") REFERENCES "user" ("id") ON DELETE CASCADE
            );
            """, "ensured refresh_token table exists");
        ExecuteRepairSql(conn, logger,
            "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_refresh_token_token\" ON \"refresh_token\" (\"token\");",
            "ensured refresh_token token unique index exists");
        ExecuteRepairSql(conn, logger,
            "CREATE INDEX IF NOT EXISTS \"IX_refresh_token_user_id\" ON \"refresh_token\" (\"user_id\");",
            "ensured refresh_token user_id index exists");
        ExecuteRepairSql(conn, logger,
            "CREATE INDEX IF NOT EXISTS \"IX_refresh_token_expires_at\" ON \"refresh_token\" (\"expires_at\");",
            "ensured refresh_token expires_at index exists");
    }

    public async Task SeedDefaultDataAsync(GamesDbContext context)
    {
        if (!context.Users.Any())
        {
            var adminUser = new User
            {
                Username = "Admin",
                PasswordHash = null,
                Role = UserRole.Admin,
                IsDefault = true
            };
            context.Users.Add(adminUser);
            await context.SaveChangesAsync();

            var platforms = new[]
            {
                new GamePlatform { Name = "Steam", Color = "#2a475e", SortOrder = 1, UserId = adminUser.Id },
                new GamePlatform { Name = "Epic Games", Color = "#2F2D2E", SortOrder = 2, UserId = adminUser.Id },
                new GamePlatform { Name = "GOG", Color = "#c99aff", SortOrder = 3, UserId = adminUser.Id },
                new GamePlatform { Name = "Itch.io", Color = "#de4660", SortOrder = 4, UserId = adminUser.Id },
                new GamePlatform { Name = "EA", Color = "#EA2020", SortOrder = 5, UserId = adminUser.Id },
                new GamePlatform { Name = "Ubisoft", Color = "#1472F1", SortOrder = 6, UserId = adminUser.Id },
                new GamePlatform { Name = "Battle.net", Color = "#009AE4", SortOrder = 7, UserId = adminUser.Id },
                new GamePlatform { Name = "Emulator", Color = "#d12e2e", SortOrder = 8, UserId = adminUser.Id },
                new GamePlatform { Name = "Nintendo Switch", Color = "#fe0016", SortOrder = 9, UserId = adminUser.Id }
            };
            context.GamePlatforms.AddRange(platforms);

            var statuses = new[]
            {
                new GameStatus { Name = "Pending", Color = "#be9c23", SortOrder = 1, UserId = adminUser.Id },
                new GameStatus { Name = "Next up", Color = "#793e77", SortOrder = 2, UserId = adminUser.Id },
                new GameStatus { Name = "DEFAULT_PLAYING", Color = "#61afef", SortOrder = 3, IsDefault = true, StatusType = SpecialStatusType.Playing, UserId = adminUser.Id },
                new GameStatus { Name = "Done", Color = "#3fc20f", SortOrder = 4, UserId = adminUser.Id },
                new GameStatus { Name = "Abandoned", Color = "#b91d1d", SortOrder = 5, UserId = adminUser.Id },
                new GameStatus { Name = "DEFAULT_NOT_FULFILLED", Color = "#919191", SortOrder = 6, IsDefault = true, StatusType = SpecialStatusType.NotFulfilled, UserId = adminUser.Id }
            };
            context.GameStatuses.AddRange(statuses);

            var playWiths = new[]
            {
                new GamePlayWith { Name = "Solo", Color = "#24c2b7", SortOrder = 1, UserId = adminUser.Id },
                new GamePlayWith { Name = "Friends", Color = "#ab32ec", SortOrder = 2, UserId = adminUser.Id },
                new GamePlayWith { Name = "Family", Color = "#099012", SortOrder = 3, UserId = adminUser.Id }
            };
            context.GamePlayWiths.AddRange(playWiths);

            var playedStatuses = new[]
            {
                new GamePlayedStatus { Name = "None", Color = "#b5b5b5", SortOrder = 1, UserId = adminUser.Id },
                new GamePlayedStatus { Name = "Some", Color = "#873ed0", SortOrder = 2, UserId = adminUser.Id },
                new GamePlayedStatus { Name = "Almost", Color = "#cc1eb5", SortOrder = 3, UserId = adminUser.Id },
                new GamePlayedStatus { Name = "Completed", Color = "#2ed42b", SortOrder = 4, UserId = adminUser.Id },
                new GamePlayedStatus { Name = "Abandoned", Color = "#a60808", SortOrder = 5, UserId = adminUser.Id }
            };
            context.GamePlayedStatuses.AddRange(playedStatuses);

            var replayTypes = new[]
            {
                new GameReplayType { Name = "Rejugado", Color = "#61afef", SortOrder = 1, IsDefault = true, ReplayType = SpecialReplayType.Replay, UserId = adminUser.Id },
                new GameReplayType { Name = "DLC", Color = "#c678dd", SortOrder = 2, UserId = adminUser.Id },
                new GameReplayType { Name = "Expansión", Color = "#98c379", SortOrder = 3, UserId = adminUser.Id },
                new GameReplayType { Name = "NG+", Color = "#e5c07b", SortOrder = 4, UserId = adminUser.Id },
                new GameReplayType { Name = "100%", Color = "#e06c75", SortOrder = 5, UserId = adminUser.Id },
                new GameReplayType { Name = "Logros", Color = "#56b6c2", SortOrder = 6, UserId = adminUser.Id }
            };
            context.GameReplayTypes.AddRange(replayTypes);

            await context.SaveChangesAsync();
        }
    }

    public async Task SeedMissingReplayTypesAsync(GamesDbContext context)
    {
        var usersWithoutReplayTypes = await context.Users
            .Where(u => !context.GameReplayTypes.Any(rt => rt.UserId == u.Id))
            .ToListAsync();

        foreach (var user in usersWithoutReplayTypes)
        {
            context.GameReplayTypes.AddRange(
                new GameReplayType { Name = "Rejugado", Color = "#61afef", SortOrder = 1, IsDefault = true, ReplayType = SpecialReplayType.Replay, UserId = user.Id },
                new GameReplayType { Name = "DLC", Color = "#c678dd", SortOrder = 2, UserId = user.Id },
                new GameReplayType { Name = "Expansión", Color = "#98c379", SortOrder = 3, UserId = user.Id },
                new GameReplayType { Name = "NG+", Color = "#e5c07b", SortOrder = 4, UserId = user.Id },
                new GameReplayType { Name = "100%", Color = "#e06c75", SortOrder = 5, UserId = user.Id },
                new GameReplayType { Name = "Logros", Color = "#56b6c2", SortOrder = 6, UserId = user.Id }
            );
        }

        if (usersWithoutReplayTypes.Any())
            await context.SaveChangesAsync();
    }

    private static void EnsureColumn(SqliteConnection conn, ILogger logger, string table, string column, string definition)
    {
        if (ColumnExists(conn, table, column))
            return;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {QuoteIdentifier(table)} ADD COLUMN {QuoteIdentifier(column)} {definition}";
        cmd.ExecuteNonQuery();
        logger.LogInformation("Schema repair: added missing column {Table}.{Column}.", table, column);
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({QuoteIdentifier(table)})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static void ExecuteRepairSql(SqliteConnection conn, ILogger logger, string sql, string description)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
        logger.LogInformation("Schema repair: {Description}.", description);
    }

    private static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
