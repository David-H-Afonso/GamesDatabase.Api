using GamesDatabase.Api.DTOs.Steam;

namespace GamesDatabase.Api.Services.Steam;

public interface ISteamSyncService
{
    Task<SteamSyncResult> SyncGameAsync(int userId, int gameId);
    Task<SteamSyncResult> SyncAllUserGamesAsync(int userId);
    Task<SteamImportResult> ImportLibraryAsync(int userId, List<int> appIds, bool createMissing);
}
