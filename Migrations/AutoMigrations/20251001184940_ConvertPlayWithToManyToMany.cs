using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations.AutoMigrations
{
    /// <inheritdoc />
    public partial class ConvertPlayWithToManyToMany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_game_game_play_with_play_with_id",
                table: "game");

            migrationBuilder.DropIndex(
                name: "IX_game_play_with_id",
                table: "game");

            migrationBuilder.DropColumn(
                name: "play_with_id",
                table: "game");

            migrationBuilder.CreateTable(
                name: "game_play_with_mapping",
                columns: table => new
                {
                    game_id = table.Column<int>(type: "INTEGER", nullable: false),
                    play_with_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_play_with_mapping", x => new { x.game_id, x.play_with_id });
                    table.ForeignKey(
                        name: "FK_game_play_with_mapping_game_game_id",
                        column: x => x.game_id,
                        principalTable: "game",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_game_play_with_mapping_game_play_with_play_with_id",
                        column: x => x.play_with_id,
                        principalTable: "game_play_with",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_play_with_mapping_play_with_id",
                table: "game_play_with_mapping",
                column: "play_with_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_play_with_mapping");

            migrationBuilder.AddColumn<int>(
                name: "play_with_id",
                table: "game",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_play_with_id",
                table: "game",
                column: "play_with_id");

            migrationBuilder.AddForeignKey(
                name: "FK_game_game_play_with_play_with_id",
                table: "game",
                column: "play_with_id",
                principalTable: "game_play_with",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
