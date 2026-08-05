using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylists : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "playlist",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    hero_url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    cover_url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    logo_url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    modified_since_export = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playlist", x => x.id);
                    table.ForeignKey(
                        name: "FK_playlist_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "playlist_item",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    playlist_id = table.Column<int>(type: "INTEGER", nullable: false),
                    game_id = table.Column<int>(type: "INTEGER", nullable: false),
                    position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playlist_item", x => x.id);
                    table.ForeignKey(
                        name: "FK_playlist_item_game_game_id",
                        column: x => x.game_id,
                        principalTable: "game",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_playlist_item_playlist_playlist_id",
                        column: x => x.playlist_id,
                        principalTable: "playlist",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_playlist_user_id_name",
                table: "playlist",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_playlist_user_id_sort_order",
                table: "playlist",
                columns: new[] { "user_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "IX_playlist_item_game_id",
                table: "playlist_item",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "IX_playlist_item_playlist_id_game_id",
                table: "playlist_item",
                columns: new[] { "playlist_id", "game_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_playlist_item_playlist_id_position",
                table: "playlist_item",
                columns: new[] { "playlist_id", "position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "playlist_item");

            migrationBuilder.DropTable(
                name: "playlist");
        }
    }
}
