using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "household_connection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    client_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    account_id = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    granted_scopes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_household_connection", x => x.id);
                    table.ForeignKey(
                        name: "FK_household_connection_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "household_access_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    connection_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    family_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_household_access_token", x => x.id);
                    table.ForeignKey(
                        name: "FK_household_access_token_household_connection_connection_id",
                        column: x => x.connection_id,
                        principalTable: "household_connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "household_authorization_code",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    connection_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    code_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    redirect_uri = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    code_challenge = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    granted_scopes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    consumed_at = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_household_authorization_code", x => x.id);
                    table.ForeignKey(
                        name: "FK_household_authorization_code_household_connection_connection_id",
                        column: x => x.connection_id,
                        principalTable: "household_connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "household_refresh_token",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "TEXT", nullable: false),
                    connection_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    family_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    token_hash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    expires_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "TEXT", nullable: true),
                    replaced_by_token_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_household_refresh_token", x => x.id);
                    table.ForeignKey(
                        name: "FK_household_refresh_token_household_connection_connection_id",
                        column: x => x.connection_id,
                        principalTable: "household_connection",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_household_refresh_token_household_refresh_token_replaced_by_token_id",
                        column: x => x.replaced_by_token_id,
                        principalTable: "household_refresh_token",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_household_access_token_connection_id_family_id",
                table: "household_access_token",
                columns: new[] { "connection_id", "family_id" });

            migrationBuilder.CreateIndex(
                name: "IX_household_access_token_expires_at",
                table: "household_access_token",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_household_access_token_token_hash",
                table: "household_access_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_household_authorization_code_code_hash",
                table: "household_authorization_code",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_household_authorization_code_connection_id",
                table: "household_authorization_code",
                column: "connection_id");

            migrationBuilder.CreateIndex(
                name: "IX_household_authorization_code_expires_at",
                table: "household_authorization_code",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_household_connection_account_id",
                table: "household_connection",
                column: "account_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_household_connection_user_id_client_id",
                table: "household_connection",
                columns: new[] { "user_id", "client_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_household_refresh_token_connection_id_family_id",
                table: "household_refresh_token",
                columns: new[] { "connection_id", "family_id" });

            migrationBuilder.CreateIndex(
                name: "IX_household_refresh_token_expires_at",
                table: "household_refresh_token",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "IX_household_refresh_token_replaced_by_token_id",
                table: "household_refresh_token",
                column: "replaced_by_token_id");

            migrationBuilder.CreateIndex(
                name: "IX_household_refresh_token_token_hash",
                table: "household_refresh_token",
                column: "token_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "household_access_token");

            migrationBuilder.DropTable(
                name: "household_authorization_code");

            migrationBuilder.DropTable(
                name: "household_refresh_token");

            migrationBuilder.DropTable(
                name: "household_connection");
        }
    }
}
