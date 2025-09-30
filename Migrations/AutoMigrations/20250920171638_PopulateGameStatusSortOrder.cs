using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations.AutoMigrations
{
    /// <inheritdoc />
    public partial class PopulateGameStatusSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Populate sort_order for existing rows ordered by name
            // SQLite doesn't allow direct ROW_NUMBER updates in older versions, so we use a temp table approach
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS temp_game_status_order (id INTEGER PRIMARY KEY, sort_order INTEGER);
                INSERT INTO temp_game_status_order (id, sort_order)
                SELECT id, ROW_NUMBER() OVER (ORDER BY name COLLATE NOCASE) as rn
                FROM game_status;

                UPDATE game_status
                SET sort_order = (
                    SELECT sort_order FROM temp_game_status_order t WHERE t.id = game_status.id
                )
                WHERE id IN (SELECT id FROM temp_game_status_order);

                DROP TABLE IF EXISTS temp_game_status_order;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
