using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGameViewExportCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "modified_since_export",
                table: "game_view",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "game_view_export_cache",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    game_view_id = table.Column<int>(type: "INTEGER", nullable: false),
                    last_exported_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    configuration_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_view_export_cache", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_view_export_cache_game_view_game_view_id",
                        column: x => x.game_view_id,
                        principalTable: "game_view",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_view_export_cache_game_view_id",
                table: "game_view_export_cache",
                column: "game_view_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_view_export_cache");

            migrationBuilder.DropColumn(
                name: "modified_since_export",
                table: "game_view");
        }
    }
}
