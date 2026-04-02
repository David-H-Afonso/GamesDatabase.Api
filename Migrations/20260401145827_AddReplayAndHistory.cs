using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReplayAndHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "game_replay_type",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    color = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "#ffffff"),
                    is_default = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    replay_type = table.Column<int>(type: "INTEGER", nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_replay_type", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_replay_type_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_history_entry",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    game_id = table.Column<int>(type: "INTEGER", nullable: true),
                    game_name = table.Column<string>(type: "TEXT", nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    field = table.Column<string>(type: "TEXT", nullable: false),
                    old_value = table.Column<string>(type: "TEXT", nullable: true),
                    new_value = table.Column<string>(type: "TEXT", nullable: true),
                    description = table.Column<string>(type: "TEXT", nullable: false),
                    action_type = table.Column<string>(type: "TEXT", nullable: false),
                    changed_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_history_entry", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_history_entry_game_game_id",
                        column: x => x.game_id,
                        principalTable: "game",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_game_history_entry_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_replay",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    game_id = table.Column<int>(type: "INTEGER", nullable: false),
                    replay_type_id = table.Column<int>(type: "INTEGER", nullable: false),
                    started = table.Column<string>(type: "TEXT", nullable: true),
                    finished = table.Column<string>(type: "TEXT", nullable: true),
                    grade = table.Column<int>(type: "INTEGER", nullable: true),
                    notes = table.Column<string>(type: "TEXT", nullable: true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_replay", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_replay_game_game_id",
                        column: x => x.game_id,
                        principalTable: "game",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_game_replay_game_replay_type_replay_type_id",
                        column: x => x.replay_type_id,
                        principalTable: "game_replay_type",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_game_replay_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_history_entry_changed_at",
                table: "game_history_entry",
                column: "changed_at");

            migrationBuilder.CreateIndex(
                name: "IX_game_history_entry_game_id",
                table: "game_history_entry",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_history_entry_user_id",
                table: "game_history_entry",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_replay_game_id",
                table: "game_replay",
                column: "game_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_replay_replay_type_id",
                table: "game_replay",
                column: "replay_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_replay_user_id",
                table: "game_replay",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_replay_type_user_id_name",
                table: "game_replay_type",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_replay_type_user_id_replay_type_is_default",
                table: "game_replay_type",
                columns: new[] { "user_id", "replay_type", "is_default" },
                unique: true,
                filter: "is_default = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_history_entry");

            migrationBuilder.DropTable(
                name: "game_replay");

            migrationBuilder.DropTable(
                name: "game_replay_type");
        }
    }
}
