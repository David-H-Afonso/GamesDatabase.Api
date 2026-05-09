using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReleasedColumnFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Use raw SQL with IF NOT EXISTS to safely handle both:
            // - CasaOS prod where 20260509001056 ran as empty (column missing)
            // - Local dev where 20260509001056 already added the column
            migrationBuilder.Sql("ALTER TABLE game_replay ADD COLUMN IF NOT EXISTS released TEXT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite does not support DROP COLUMN in older versions; no-op is safe here
        }
    }
}
