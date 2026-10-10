using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistMaxLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "max_length",
                table: "playlist",
                type: "INTEGER",
                nullable: true);

            // Existing Focus lists gain the same capacity as newly created ones.
            // Keep all items: over-capacity lists remain editable but cannot add.
            migrationBuilder.Sql("UPDATE playlist SET max_length = 4 WHERE lower(trim(name)) = 'focus' AND is_automatic = 0 AND max_length IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "max_length",
                table: "playlist");
        }
    }
}
