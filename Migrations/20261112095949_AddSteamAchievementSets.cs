using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamAchievementSets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "archived",
                table: "steam_achievement",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "global_percent",
                table: "steam_achievement",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "group_id",
                table: "steam_achievement",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "internal_key",
                table: "steam_achievement",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "steam_achievement",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<double>(
                name: "progress_current",
                table: "steam_achievement",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "progress_max",
                table: "steam_achievement",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "progress_min",
                table: "steam_achievement",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "progress_type",
                table: "steam_achievement",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "steam_achievement_schema_hash",
                table: "game",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "steam_achievement_schema_version",
                table: "game",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "steam_achievements_last_error",
                table: "game",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "steam_achievements_source",
                table: "game",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "steam_achievements_synced_at",
                table: "game",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "steam_completion_preserved",
                table: "game",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Snapshot the old full-completion predicate BEFORE the first sets sync.
            // Never infer completion from game.completion (that value is hours).
            migrationBuilder.Sql("""
                UPDATE "game" SET "steam_completion_preserved" = 1
                WHERE EXISTS (
                    SELECT 1 FROM "steam_achievement" a
                    WHERE a."user_id" = "game"."user_id" AND a."game_id" = "game"."id"
                ) AND NOT EXISTS (
                    SELECT 1 FROM "steam_achievement" a
                    WHERE a."user_id" = "game"."user_id" AND a."game_id" = "game"."id" AND a."achieved" = 0
                );
                """);

            migrationBuilder.CreateTable(
                name: "steam_achievement_set",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    game_id = table.Column<int>(type: "INTEGER", nullable: true),
                    steam_app_id = table.Column<int>(type: "INTEGER", nullable: false),
                    group_id = table.Column<int>(type: "INTEGER", nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    dlc_app_id = table.Column<int>(type: "INTEGER", nullable: true),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false),
                    archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    developer_only = table.Column<bool>(type: "INTEGER", nullable: false),
                    is_public = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    total_achievements = table.Column<int>(type: "INTEGER", nullable: false),
                    completion_achievements = table.Column<int>(type: "INTEGER", nullable: false),
                    is_achievable = table.Column<bool>(type: "INTEGER", nullable: true),
                    is_completed = table.Column<bool>(type: "INTEGER", nullable: false),
                    completed_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_synced = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_achievement_set", x => x.id);
                    table.ForeignKey(
                        name: "FK_steam_achievement_set_game_game_id",
                        column: x => x.game_id,
                        principalTable: "game",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_steam_achievement_set_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_steam_achievement_set_game_id",
                table: "steam_achievement_set",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "IX_steam_achievement_set_user_id_steam_app_id_group_id",
                table: "steam_achievement_set",
                columns: new[] { "user_id", "steam_app_id", "group_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steam_achievement_set");

            migrationBuilder.DropColumn(
                name: "archived",
                table: "steam_achievement");

            migrationBuilder.DropColumn(
                name: "global_percent",
                table: "steam_achievement");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "steam_achievement");

            migrationBuilder.DropColumn(
                name: "internal_key",
                table: "steam_achievement");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "steam_achievement");

            migrationBuilder.DropColumn(
                name: "progress_current",
                table: "steam_achievement");

            migrationBuilder.DropColumn(
                name: "progress_max",
                table: "steam_achievement");

            migrationBuilder.DropColumn(
                name: "progress_min",
                table: "steam_achievement");

            migrationBuilder.DropColumn(
                name: "progress_type",
                table: "steam_achievement");

            migrationBuilder.DropColumn(
                name: "steam_achievement_schema_hash",
                table: "game");

            migrationBuilder.DropColumn(
                name: "steam_achievement_schema_version",
                table: "game");

            migrationBuilder.DropColumn(
                name: "steam_achievements_last_error",
                table: "game");

            migrationBuilder.DropColumn(
                name: "steam_achievements_source",
                table: "game");

            migrationBuilder.DropColumn(
                name: "steam_achievements_synced_at",
                table: "game");

            migrationBuilder.DropColumn(
                name: "steam_completion_preserved",
                table: "game");
        }
    }
}
