using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    [Migration("20260608143500_AddDuplicateGameDismissals")]
    public partial class AddDuplicateGameDismissals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "duplicate_game_dismissal",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    game_id_a = table.Column<int>(type: "INTEGER", nullable: false),
                    game_id_b = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_duplicate_game_dismissal", x => x.id);
                    table.ForeignKey(
                        name: "FK_duplicate_game_dismissal_game_game_id_a",
                        column: x => x.game_id_a,
                        principalTable: "game",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_duplicate_game_dismissal_game_game_id_b",
                        column: x => x.game_id_b,
                        principalTable: "game",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_duplicate_game_dismissal_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_duplicate_game_dismissal_game_id_a",
                table: "duplicate_game_dismissal",
                column: "game_id_a");

            migrationBuilder.CreateIndex(
                name: "IX_duplicate_game_dismissal_game_id_b",
                table: "duplicate_game_dismissal",
                column: "game_id_b");

            migrationBuilder.CreateIndex(
                name: "IX_duplicate_game_dismissal_user_id_game_id_a_game_id_b",
                table: "duplicate_game_dismissal",
                columns: new[] { "user_id", "game_id_a", "game_id_b" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "duplicate_game_dismissal");
        }
    }
}
