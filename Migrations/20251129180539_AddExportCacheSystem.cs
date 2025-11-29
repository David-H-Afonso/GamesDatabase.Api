using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExportCacheSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "modified_since_export",
                table: "game",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "game_export_cache",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    game_id = table.Column<int>(type: "INTEGER", nullable: false),
                    last_exported_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    logo_downloaded = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    cover_downloaded = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    logo_url = table.Column<string>(type: "TEXT", nullable: true),
                    cover_url = table.Column<string>(type: "TEXT", nullable: true),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_export_cache", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_export_cache_game_game_id",
                        column: x => x.game_id,
                        principalTable: "game",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_export_cache_game_id",
                table: "game_export_cache",
                column: "game_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_export_cache");

            migrationBuilder.DropColumn(
                name: "modified_since_export",
                table: "game");
        }
    }
}
