using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSortOrderToGameView : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "game_view",
                type: "INTEGER",
                nullable: false,
                defaultValue: 999);

            // Inicializar SortOrder basado en el orden alfabético de nombre
            migrationBuilder.Sql(@"
                UPDATE game_view 
                SET SortOrder = (
                    SELECT COUNT(*) + 1 
                    FROM game_view AS gv2 
                    WHERE gv2.user_id = game_view.user_id 
                    AND LOWER(gv2.name) < LOWER(game_view.name)
                )
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "game_view");
        }
    }
}
