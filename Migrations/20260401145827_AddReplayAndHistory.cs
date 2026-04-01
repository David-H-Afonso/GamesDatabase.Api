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
                name: "user",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    username = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "TEXT", nullable: true),
                    role = table.Column<int>(type: "INTEGER", nullable: false),
                    is_default = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    use_score_colors = table.Column<bool>(type: "INTEGER", nullable: false),
                    score_provider = table.Column<string>(type: "TEXT", nullable: false),
                    show_price_comparison_icon = table.Column<bool>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "game_platform",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    color = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "#ffffff"),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_platform", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_platform_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_play_with",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    color = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "#ffffff"),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_play_with", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_play_with_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_played_status",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    color = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "#ffffff"),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_played_status", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_played_status_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "game_status",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    color = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "#ffffff"),
                    is_default = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    status_type = table.Column<int>(type: "INTEGER", nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_status", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_status_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game_view",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    filters_json = table.Column<string>(type: "TEXT", nullable: false),
                    sorting_json = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    is_public = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    created_by = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    modified_since_export = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game_view", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_view_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "game",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    status_id = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    grade = table.Column<int>(type: "INTEGER", nullable: true),
                    critic = table.Column<int>(type: "INTEGER", nullable: true),
                    critic_provider = table.Column<string>(type: "TEXT", nullable: true),
                    story = table.Column<int>(type: "INTEGER", nullable: true),
                    completion = table.Column<int>(type: "INTEGER", nullable: true),
                    score = table.Column<decimal>(type: "TEXT", nullable: true),
                    platform_id = table.Column<int>(type: "INTEGER", nullable: true),
                    released = table.Column<string>(type: "TEXT", nullable: true),
                    started = table.Column<string>(type: "TEXT", nullable: true),
                    finished = table.Column<string>(type: "TEXT", nullable: true),
                    comment = table.Column<string>(type: "TEXT", nullable: true),
                    played_status_id = table.Column<int>(type: "INTEGER", nullable: true),
                    logo = table.Column<string>(type: "TEXT", nullable: true),
                    cover = table.Column<string>(type: "TEXT", nullable: true),
                    IsCheaperByKey = table.Column<bool>(type: "INTEGER", nullable: true),
                    KeyStoreUrl = table.Column<string>(type: "TEXT", nullable: true),
                    modified_since_export = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_game", x => x.id);
                    table.ForeignKey(
                        name: "FK_game_game_platform_platform_id",
                        column: x => x.platform_id,
                        principalTable: "game_platform",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_game_game_played_status_played_status_id",
                        column: x => x.played_status_id,
                        principalTable: "game_played_status",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_game_game_status_status_id",
                        column: x => x.status_id,
                        principalTable: "game_status",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_game_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_game_platform_id",
                table: "game",
                column: "platform_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_played_status_id",
                table: "game",
                column: "played_status_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_status_id",
                table: "game",
                column: "status_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_user_id",
                table: "game",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_export_cache_game_id",
                table: "game_export_cache",
                column: "game_id",
                unique: true);

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
                name: "IX_game_platform_user_id_name",
                table: "game_platform",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_play_with_user_id_name",
                table: "game_play_with",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_play_with_mapping_play_with_id",
                table: "game_play_with_mapping",
                column: "play_with_id");

            migrationBuilder.CreateIndex(
                name: "IX_game_played_status_user_id_name",
                table: "game_played_status",
                columns: new[] { "user_id", "name" },
                unique: true);

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

            migrationBuilder.CreateIndex(
                name: "IX_game_status_user_id_name",
                table: "game_status",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_status_user_id_status_type_is_default",
                table: "game_status",
                columns: new[] { "user_id", "status_type", "is_default" },
                unique: true,
                filter: "is_default = 1");

            migrationBuilder.CreateIndex(
                name: "IX_game_view_user_id_name",
                table: "game_view",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_view_export_cache_game_view_id",
                table: "game_view_export_cache",
                column: "game_view_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_username",
                table: "user",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "game_export_cache");

            migrationBuilder.DropTable(
                name: "game_history_entry");

            migrationBuilder.DropTable(
                name: "game_play_with_mapping");

            migrationBuilder.DropTable(
                name: "game_replay");

            migrationBuilder.DropTable(
                name: "game_view_export_cache");

            migrationBuilder.DropTable(
                name: "game_play_with");

            migrationBuilder.DropTable(
                name: "game");

            migrationBuilder.DropTable(
                name: "game_replay_type");

            migrationBuilder.DropTable(
                name: "game_view");

            migrationBuilder.DropTable(
                name: "game_platform");

            migrationBuilder.DropTable(
                name: "game_played_status");

            migrationBuilder.DropTable(
                name: "game_status");

            migrationBuilder.DropTable(
                name: "user");
        }
    }
}
