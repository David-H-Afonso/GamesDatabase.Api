using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIsManuallyCompleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsManuallyCompleted",
                table: "game",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsManuallyCompleted",
                table: "game");
        }
    }
}
