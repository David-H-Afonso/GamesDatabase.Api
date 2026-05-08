using GamesDatabase.Api.DTOs.Steam;

namespace GamesDatabase.Api.Services.Steam;

public interface ISteamStoreService
{
    Task<SteamAppDetailsDto?> GetOrCacheAppDetailsAsync(int appId);
    Task<SteamReviewSummaryDto?> GetReviewSummaryAsync(int appId);
}
