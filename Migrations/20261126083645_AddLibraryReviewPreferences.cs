using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLibraryReviewPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "last_used_at",
                table: "game_view",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "metadata_version",
                table: "game_export_cache",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "manual_playtime_is_additive",
                table: "game",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reviewed after EF generation: its SQLite table rebuild resets a
            // sequence when the highest ID was deleted. The bundled SQLite
            // supports native DROP COLUMN; none of these columns has an index/FK.
            migrationBuilder.Sql("ALTER TABLE \"game_view\" DROP COLUMN \"last_used_at\";");
            migrationBuilder.Sql("ALTER TABLE \"game_export_cache\" DROP COLUMN \"metadata_version\";");
            migrationBuilder.Sql("ALTER TABLE \"game\" DROP COLUMN \"manual_playtime_is_additive\";");
        }
    }
}
