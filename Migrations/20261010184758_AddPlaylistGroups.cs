using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaylistGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "group_id",
                table: "playlist",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "playlist_group",
                columns: table => new
                {
                    id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    user_id = table.Column<int>(type: "INTEGER", nullable: false),
                    name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    sort_order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_playlist_group", x => x.id);
                    table.ForeignKey(
                        name: "FK_playlist_group_user_user_id",
                        column: x => x.user_id,
                        principalTable: "user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_playlist_group_id",
                table: "playlist",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "IX_playlist_group_user_id_name",
                table: "playlist_group",
                columns: new[] { "user_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_playlist_playlist_group_group_id",
                table: "playlist",
                column: "group_id",
                principalTable: "playlist_group",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_playlist_playlist_group_group_id",
                table: "playlist");

            migrationBuilder.DropTable(
                name: "playlist_group");

            migrationBuilder.DropIndex(
                name: "IX_playlist_group_id",
                table: "playlist");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "playlist");
        }
    }
}
