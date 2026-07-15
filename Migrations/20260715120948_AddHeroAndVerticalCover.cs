using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHeroAndVerticalCover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "hero_downloaded",
                table: "game_export_cache",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "hero_url",
                table: "game_export_cache",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "hero",
                table: "game",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql("UPDATE game SET hero = cover WHERE hero IS NULL");
            migrationBuilder.Sql("UPDATE game SET cover = NULL");
            migrationBuilder.Sql("UPDATE game_export_cache SET hero_url = cover_url WHERE hero_url IS NULL");
            migrationBuilder.Sql("UPDATE game_export_cache SET hero_downloaded = cover_downloaded");
            migrationBuilder.Sql("UPDATE game_export_cache SET cover_url = NULL, cover_downloaded = 0");
            migrationBuilder.Sql("UPDATE game_view SET filters_json = replace(filters_json, '\"Cover\"', '\"Hero\"') WHERE filters_json IS NOT NULL");
            migrationBuilder.Sql("UPDATE game_view SET sorting_json = replace(sorting_json, '\"Cover\"', '\"Hero\"') WHERE sorting_json IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE game SET cover = hero WHERE cover IS NULL");
            migrationBuilder.Sql("UPDATE game_export_cache SET cover_url = hero_url WHERE cover_url IS NULL");
            migrationBuilder.Sql("UPDATE game_export_cache SET cover_downloaded = hero_downloaded");
            migrationBuilder.Sql("UPDATE game_view SET filters_json = replace(filters_json, '\"Hero\"', '\"Cover\"') WHERE filters_json IS NOT NULL");
            migrationBuilder.Sql("UPDATE game_view SET sorting_json = replace(sorting_json, '\"Hero\"', '\"Cover\"') WHERE sorting_json IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "hero_downloaded",
                table: "game_export_cache");

            migrationBuilder.DropColumn(
                name: "hero_url",
                table: "game_export_cache");

            migrationBuilder.DropColumn(
                name: "hero",
                table: "game");
        }
    }
}
