using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSpecialStatusSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Color",
                table: "game_status",
                newName: "color");

            migrationBuilder.RenameColumn(
                name: "Color",
                table: "game_played_status",
                newName: "color");

            migrationBuilder.RenameColumn(
                name: "Color",
                table: "game_play_with",
                newName: "color");

            migrationBuilder.RenameColumn(
                name: "Color",
                table: "game_platform",
                newName: "color");

            migrationBuilder.AlterColumn<string>(
                name: "color",
                table: "game_status",
                type: "TEXT",
                nullable: false,
                defaultValue: "#ffffff",
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AddColumn<bool>(
                name: "is_default",
                table: "game_status",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "status_type",
                table: "game_status",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "color",
                table: "game_played_status",
                type: "TEXT",
                nullable: false,
                defaultValue: "#ffffff",
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "color",
                table: "game_play_with",
                type: "TEXT",
                nullable: false,
                defaultValue: "#ffffff",
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<string>(
                name: "color",
                table: "game_platform",
                type: "TEXT",
                nullable: false,
                defaultValue: "#ffffff",
                oldClrType: typeof(string),
                oldType: "TEXT");

            migrationBuilder.CreateIndex(
                name: "IX_game_status_status_type_is_default",
                table: "game_status",
                columns: new[] { "status_type", "is_default" },
                unique: true,
                filter: "is_default = 1");

            // Seed the default "Not Fulfilled" status if no default status exists
            migrationBuilder.Sql(@"
                -- First, check if we need to create or update a default status
                INSERT OR IGNORE INTO game_status (name, sort_order, is_active, color, is_default, status_type)
                SELECT 'Not Fulfilled (Default)', 0, 1, '#ff6b6b', 1, 1
                WHERE NOT EXISTS (
                    SELECT 1 FROM game_status WHERE is_default = 1 AND status_type = 1
                );
                
                -- If we still don't have a default status, update the first existing status
                UPDATE game_status 
                SET is_default = 1, status_type = 1
                WHERE id = (
                    SELECT MIN(id) FROM game_status 
                    WHERE NOT EXISTS (
                        SELECT 1 FROM game_status WHERE is_default = 1 AND status_type = 1
                    )
                )
                AND NOT EXISTS (
                    SELECT 1 FROM game_status WHERE is_default = 1 AND status_type = 1
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_game_status_status_type_is_default",
                table: "game_status");

            migrationBuilder.DropColumn(
                name: "is_default",
                table: "game_status");

            migrationBuilder.DropColumn(
                name: "status_type",
                table: "game_status");

            migrationBuilder.RenameColumn(
                name: "color",
                table: "game_status",
                newName: "Color");

            migrationBuilder.RenameColumn(
                name: "color",
                table: "game_played_status",
                newName: "Color");

            migrationBuilder.RenameColumn(
                name: "color",
                table: "game_play_with",
                newName: "Color");

            migrationBuilder.RenameColumn(
                name: "color",
                table: "game_platform",
                newName: "Color");

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "game_status",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldDefaultValue: "#ffffff");

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "game_played_status",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldDefaultValue: "#ffffff");

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "game_play_with",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldDefaultValue: "#ffffff");

            migrationBuilder.AlterColumn<string>(
                name: "Color",
                table: "game_platform",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldDefaultValue: "#ffffff");
        }
    }
}
