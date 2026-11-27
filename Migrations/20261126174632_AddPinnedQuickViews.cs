using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPinnedQuickViews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "pinned_slot",
                table: "game_view",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_view_user_id_pinned_slot",
                table: "game_view",
                columns: new[] { "user_id", "pinned_slot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_game_view_user_id_pinned_slot",
                table: "game_view");

            // Native DROP COLUMN avoids rebuilding the table and losing the
            // SQLite AUTOINCREMENT high-water mark on a rehearsed downgrade.
            migrationBuilder.Sql("ALTER TABLE game_view DROP COLUMN pinned_slot;");
        }
    }
}
