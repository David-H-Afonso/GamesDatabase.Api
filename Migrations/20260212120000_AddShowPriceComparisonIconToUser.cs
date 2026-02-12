using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(GamesDatabase.Api.Data.GamesDbContext))]
    [Migration("20260212120000_AddShowPriceComparisonIconToUser")]
    public partial class AddShowPriceComparisonIconToUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "show_price_comparison_icon",
                table: "user",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "show_price_comparison_icon",
                table: "user");
        }
    }
}
