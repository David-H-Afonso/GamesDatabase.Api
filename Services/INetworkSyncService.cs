namespace GamesDatabase.Api.Services;

public interface INetworkSyncService
{
    Task<NetworkSyncResult> SyncToNetworkAsync(int userId, string? authorizationHeader, bool fullSync = false);
    Task<FolderAnalysisResult> AnalyzeFoldersAsync(int userId);
}
