using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddMetadataAutofillJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "metadata_autofill_job",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    client_request_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    request_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    active_user_id = table.Column<int>(type: "INTEGER", nullable: true),
                    overwrite = table.Column<bool>(type: "INTEGER", nullable: false),
                    accept_listed_platform_critic = table.Column<bool>(type: "INTEGER", nullable: false),
                    state = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    items_json = table.Column<string>(type: "TEXT", nullable: false),
                    current_phase = table.Column<string>(type: "TEXT", maxLength: 24, nullable: true),
                    current_game_id = table.Column<int>(type: "INTEGER", nullable: true),
                    cancel_requested = table.Column<bool>(type: "INTEGER", nullable: false),
                    dismissed = table.Column<bool>(type: "INTEGER", nullable: false),
                    revision = table.Column<long>(type: "INTEGER", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    finished_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    next_attempt_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    lease_owner = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    lease_until = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_metadata_autofill_job", x => x.id);
                    table.CheckConstraint("CK_metadata_job_active_owner", "active_user_id IS NULL OR active_user_id = user_id");
                    table.ForeignKey(
                        name: "FK_metadata_autofill_job_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_metadata_autofill_job_active_user_id",
                table: "metadata_autofill_job",
                column: "active_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_metadata_autofill_job_active_user_id_next_attempt_at",
                table: "metadata_autofill_job",
                columns: new[] { "active_user_id", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_metadata_autofill_job_user_id_client_request_id",
                table: "metadata_autofill_job",
                columns: new[] { "user_id", "client_request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "metadata_autofill_job");
        }
    }
}
