using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class SteamIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "steam_avatar_url",
                table: "user",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "steam_id",
                table: "user",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "steam_linked_at",
                table: "user",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "steam_nickname",
                table: "user",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "steam_app_id",
                table: "game",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "steam_last_synced",
                table: "game",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "steam_playtime_2weeks",
                table: "game",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "steam_playtime_forever",
                table: "game",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "steam_achievement",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    game_id = table.Column<int>(type: "INTEGER", nullable: true),
                    steam_app_id = table.Column<int>(type: "INTEGER", nullable: false),
                    api_name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    display_name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "TEXT", nullable: true),
                    achieved = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    unlock_time = table.Column<DateTime>(type: "TEXT", nullable: true),
                    icon_url = table.Column<string>(type: "TEXT", nullable: true),
                    icon_gray_url = table.Column<string>(type: "TEXT", nullable: true),
                    hidden = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    last_synced = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_achievement", x => x.id);
                    table.ForeignKey(
                        name: "FK_steam_achievement_game_game_id",
                        column: x => x.game_id,
                        principalTable: "game",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_steam_achievement_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "steam_app_cache",
                columns: table => new
                {
                    app_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    developer = table.Column<string>(type: "TEXT", nullable: true),
                    publisher = table.Column<string>(type: "TEXT", nullable: true),
                    genres_json = table.Column<string>(type: "TEXT", nullable: true),
                    categories_json = table.Column<string>(type: "TEXT", nullable: true),
                    release_date = table.Column<string>(type: "TEXT", nullable: true),
                    metacritic_score = table.Column<int>(type: "INTEGER", nullable: true),
                    header_image_url = table.Column<string>(type: "TEXT", nullable: true),
                    background_image_url = table.Column<string>(type: "TEXT", nullable: true),
                    price = table.Column<string>(type: "TEXT", nullable: true),
                    is_free = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    last_fetched = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_steam_app_cache", x => x.app_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_steam_achievement_game_id",
                table: "steam_achievement",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "IX_steam_achievement_user_id_steam_app_id_api_name",
                table: "steam_achievement",
                columns: new[] { "user_id", "steam_app_id", "api_name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "steam_achievement");

            migrationBuilder.DropTable(
                name: "steam_app_cache");

            migrationBuilder.DropColumn(
                name: "steam_avatar_url",
                table: "user");

            migrationBuilder.DropColumn(
                name: "steam_id",
                table: "user");

            migrationBuilder.DropColumn(
                name: "steam_linked_at",
                table: "user");

            migrationBuilder.DropColumn(
                name: "steam_nickname",
                table: "user");

            migrationBuilder.DropColumn(
                name: "steam_app_id",
                table: "game");

            migrationBuilder.DropColumn(
                name: "steam_last_synced",
                table: "game");

            migrationBuilder.DropColumn(
                name: "steam_playtime_2weeks",
                table: "game");

            migrationBuilder.DropColumn(
                name: "steam_playtime_forever",
                table: "game");
        }
    }
}
