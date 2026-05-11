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
            // Intentionally no-op. Program.cs runs an idempotent schema repair for
            // Steam columns/tables because some desktop databases can be left with
            // this migration partially applied after a startup interruption.
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
