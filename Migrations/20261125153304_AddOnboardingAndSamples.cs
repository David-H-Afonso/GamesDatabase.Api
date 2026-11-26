using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingAndSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_game_user_id",
                table: "game");

            migrationBuilder.AddColumn<bool>(
                name: "tutorial_enabled",
                table: "user",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "sample_batch_id",
                table: "playlist",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sample_artwork_key",
                table: "game",
                type: "TEXT",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sample_batch_id",
                table: "game",
                type: "TEXT",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "onboarding_state",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    step = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    completed_version = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    sample_batch_id = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true),
                    sample_status_id = table.Column<int>(type: "INTEGER", nullable: true),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_onboarding_state", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_onboarding_state_game_status_sample_status_id",
                        column: x => x.sample_status_id,
                        principalTable: "game_status",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_onboarding_state_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_game_user_id_sample_batch_id",
                table: "game",
                columns: new[] { "user_id", "sample_batch_id" });

            migrationBuilder.CreateIndex(
                name: "IX_onboarding_state_sample_status_id",
                table: "onboarding_state",
                column: "sample_status_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "onboarding_state");

            migrationBuilder.DropIndex(
                name: "IX_game_user_id_sample_batch_id",
                table: "game");

            migrationBuilder.DropColumn(
                name: "tutorial_enabled",
                table: "user");

            migrationBuilder.DropColumn(
                name: "sample_batch_id",
                table: "playlist");

            migrationBuilder.DropColumn(
                name: "sample_artwork_key",
                table: "game");

            migrationBuilder.DropColumn(
                name: "sample_batch_id",
                table: "game");

            migrationBuilder.CreateIndex(
                name: "IX_game_user_id",
                table: "game",
                column: "user_id");
        }
    }
}
