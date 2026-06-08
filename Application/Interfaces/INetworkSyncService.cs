using GamesDatabase.Api.Application.Services;
namespace GamesDatabase.Api.Application.Interfaces;

public interface INetworkSyncService
{
    Task<NetworkSyncResult> SyncToNetworkAsync(int userId, string? authorizationHeader, bool fullSync = false);
    Task<FolderAnalysisResult> AnalyzeFoldersAsync(int userId);
    Task<DatabaseDuplicatesResult> AnalyzeDatabaseDuplicatesAsync(int userId);
    Task<int> DismissDuplicateGamesAsync(int userId, IReadOnlyCollection<int> gameIds);
}
