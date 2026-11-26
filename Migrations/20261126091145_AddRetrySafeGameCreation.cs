using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRetrySafeGameCreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "creation_request_hash",
                table: "game",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "creation_request_id",
                table: "game",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_game_user_id_creation_request_id",
                table: "game",
                columns: new[] { "user_id", "creation_request_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_game_user_id_creation_request_id",
                table: "game");

            // As in AddCollectionExperience: native DROP avoids rebuilding game
            // and losing its AUTOINCREMENT high-water mark on a downgrade.
            migrationBuilder.Sql("ALTER TABLE \"game\" DROP COLUMN \"creation_request_hash\";");
            migrationBuilder.Sql("ALTER TABLE \"game\" DROP COLUMN \"creation_request_id\";");
        }
    }
}
