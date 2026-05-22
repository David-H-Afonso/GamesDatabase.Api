using GamesDatabase.Api.Contracts.Steam;

namespace GamesDatabase.Api.Application.Interfaces;

public interface ISteamSyncService
{
    Task<SteamSyncResult> SyncGameAsync(int userId, int gameId);
    Task<SteamSyncResult> SyncAllUserGamesAsync(int userId);
    Task<SteamImportResult> ImportLibraryAsync(int userId, SteamImportRequest request);
    Task<SteamImportedGameDto> AddStoreGameAsync(int userId, int appId, string? logoUrl = null, string? coverUrl = null);
}
