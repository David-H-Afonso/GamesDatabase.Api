using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GamesDatabase.Api.Migrations.AutoMigrations
{
    /// <inheritdoc />
    public partial class SeedDefaultCatalogData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Esta migración está diseñada para insertar datos por defecto SOLO en bases de datos nuevas
            // Si la base de datos ya tiene datos, esta migración no hace nada
            
            // Nota: En bases de datos existentes, esta migración simplemente se marca como aplicada
            // sin insertar datos duplicados. Los datos por defecto se insertarán automáticamente
            // en nuevas instalaciones cuando las tablas estén vacías.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Eliminar solo los datos que se agregaron en esta migración
            migrationBuilder.Sql(@"
                DELETE FROM game_platform WHERE name IN ('Battle.net', 'EA', 'Emulador', 'Epic Games', 'GOG', 'Itch.io', 'Steam', 'Switch', 'Ubisoft');
                DELETE FROM game_status WHERE name IN ('None', 'Some', 'Almost', 'Completed', 'Abandoned');
            ");
        }
    }
}
