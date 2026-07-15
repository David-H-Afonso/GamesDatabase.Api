using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Controllers;
using GamesDatabase.Api.Application.Services;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IGameImportExportService
{
    Task<UpdateImageUrlsResult> UpdateImageUrlsAsync(int userId, string? imageBaseUrl, string? networkSyncPath, string? configImageBaseUrl, string? networkUsername, string? networkPassword);
    Task<CopyCoverToHeroResult> CopyCoverToHeroAsync(int userId, bool overwriteExistingHero = false);
    Task<FolderAnalysisResult> AnalyzeFoldersAsync(int userId);
    Task<DatabaseDuplicatesResult> AnalyzeDuplicateGamesAsync(int userId);
    Task<byte[]> ExportFullDatabaseAsync(int userId);
    Task<object> ImportFullDatabaseAsync(Stream csvStream, int userId);
    Task<byte[]> SelectiveExportGamesAsync(SelectiveExportRequest request, int userId);
    Task<SelectiveImportResult> SelectiveImportGamesAsync(Stream? csvFileStream, string? csvText, string? configJson, int userId);
    void ClearImageCache(string networkSyncPath);
}
