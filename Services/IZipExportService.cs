namespace GamesDatabase.Api.Services;

public interface IZipExportService
{
    Task<ZipExportResult> BuildZipAsync(string? authorizationHeader, bool fullExport = false);
}
