using GamesDatabase.Api.DTOs.Steam;

namespace GamesDatabase.Api.Services.Steam;

public interface ISteamApiService
{
    Task<List<SteamOwnedGameDto>> GetOwnedGamesAsync(string steamId);
    Task<SteamPlayerAchievementsResult> GetPlayerAchievementsAsync(string steamId, int appId);
    Task<SteamGameSchemaDto?> GetGameSchemaAsync(int appId);
    Task<SteamPlayerSummaryDto?> GetPlayerSummaryAsync(string steamId);
}
