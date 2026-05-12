using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSteamFinishedMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "steam_finished_source",
                table: "game",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "steam_finished_last_value",
                table: "game",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "steam_finished_synced_at",
                table: "game",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "steam_finished_rejected_value",
                table: "game",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "steam_finished_source",
                table: "game");

            migrationBuilder.DropColumn(
                name: "steam_finished_last_value",
                table: "game");

            migrationBuilder.DropColumn(
                name: "steam_finished_synced_at",
                table: "game");

            migrationBuilder.DropColumn(
                name: "steam_finished_rejected_value",
                table: "game");
        }
    }
}
