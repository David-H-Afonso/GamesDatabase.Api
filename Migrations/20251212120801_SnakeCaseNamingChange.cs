using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class SnakeCaseNamingChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UseScoreColors",
                table: "user",
                newName: "use_score_colors");

            migrationBuilder.RenameColumn(
                name: "ScoreProvider",
                table: "user",
                newName: "score_provider");

            migrationBuilder.RenameColumn(
                name: "CriticProvider",
                table: "game",
                newName: "critic_provider");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "use_score_colors",
                table: "user",
                newName: "UseScoreColors");

            migrationBuilder.RenameColumn(
                name: "score_provider",
                table: "user",
                newName: "ScoreProvider");

            migrationBuilder.RenameColumn(
                name: "critic_provider",
                table: "game",
                newName: "CriticProvider");
        }
    }
}
