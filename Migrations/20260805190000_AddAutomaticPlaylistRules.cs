using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations;

[Migration("20260805190000_AddAutomaticPlaylistRules")]
public partial class AddAutomaticPlaylistRules : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "is_automatic", table: "playlist", type: "INTEGER", nullable: false, defaultValue: false);
        migrationBuilder.AddColumn<string>(name: "rules_json", table: "playlist", type: "TEXT", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "is_automatic", table: "playlist");
        migrationBuilder.DropColumn(name: "rules_json", table: "playlist");
    }
}
