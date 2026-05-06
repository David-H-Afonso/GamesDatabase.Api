using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "backup_schedule",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    is_enabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    backup_hour = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 3),
                    backup_minute = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                    backup_type = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "full"),
                    destination_path = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "/backups"),
                    retention_count = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 7),
                    last_run_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    last_run_status = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "never"),
                    last_run_message = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backup_schedule", x => x.id);
                    table.ForeignKey(
                        name: "FK_backup_schedule_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_backup_schedule_user_id",
                table: "backup_schedule",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backup_schedule");
        }
    }
}
