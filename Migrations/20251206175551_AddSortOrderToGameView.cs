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
                WITH numbered AS (
                    SELECT id, ROW_NUMBER() OVER (PARTITION BY user_id ORDER BY LOWER(name)) AS rn
                    FROM game_view
                )
                UPDATE game_view
                SET SortOrder = (
                    SELECT rn FROM numbered WHERE numbered.id = game_view.id
                );
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
