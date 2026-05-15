using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddManualPlaytimeAndPlatformLogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "logo",
                table: "game_platform",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "manual_playtime_minutes",
                table: "game",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "logo",
                table: "game_platform");

            migrationBuilder.DropColumn(
                name: "manual_playtime_minutes",
                table: "game");
        }
    }
}
