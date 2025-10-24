using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceComparisonFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsCheaperByKey",
                table: "game",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KeyStoreUrl",
                table: "game",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCheaperByKey",
                table: "game");

            migrationBuilder.DropColumn(
                name: "KeyStoreUrl",
                table: "game");
        }
    }
}
