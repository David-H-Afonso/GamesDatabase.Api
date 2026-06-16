using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refresh_token",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    token = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    revoked = table.Column<bool>(type: "INTEGER", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_token", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_token_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_expires_at",
                table: "refresh_token",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_token",
                table: "refresh_token",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_user_id",
                table: "refresh_token",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_token");
        }
    }
}
