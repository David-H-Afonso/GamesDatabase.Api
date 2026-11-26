using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCollectionExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "developer",
                table: "game",
                type: "TEXT",
                maxLength: 150,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "collection_preferences",
                columns: table => new
                {
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    home_config_json = table.Column<string>(type: "TEXT", nullable: false),
                    goty_selections_json = table.Column<string>(type: "TEXT", nullable: false),
                    revision = table.Column<int>(type: "INTEGER", nullable: false),
                    updated_at = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_collection_preferences", x => x.user_id);
                    table.ForeignKey(
                        name: "FK_collection_preferences_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "collection_preferences");

            // The bundled SQLite supports DROP COLUMN. Avoid rebuilding the large
            // game table and toggling foreign keys outside the migration transaction.
            migrationBuilder.Sql("ALTER TABLE \"game\" DROP COLUMN \"developer\";");
        }
    }
}
