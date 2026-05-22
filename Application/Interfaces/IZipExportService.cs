using GamesDatabase.Api.Application.Services;
namespace GamesDatabase.Api.Application.Interfaces;

public interface IZipExportService
{
    Task<ZipExportResult> BuildZipAsync(string? authorizationHeader, bool fullExport = false);
}
