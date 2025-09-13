using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddColorToLookupTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Logo",
                table: "game",
                newName: "logo");

            migrationBuilder.RenameColumn(
                name: "Cover",
                table: "game",
                newName: "cover");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "game_status",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "game_played_status",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "game_play_with",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Color",
                table: "game_platform",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Color",
                table: "game_status");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "game_played_status");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "game_play_with");

            migrationBuilder.DropColumn(
                name: "Color",
                table: "game_platform");

            migrationBuilder.RenameColumn(
                name: "logo",
                table: "game",
                newName: "Logo");

            migrationBuilder.RenameColumn(
                name: "cover",
                table: "game",
                newName: "Cover");
        }
    }
}
