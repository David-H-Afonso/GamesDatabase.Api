using GamesDatabase.Api.Contracts.Steam;

namespace GamesDatabase.Api.Application.Interfaces;

public interface ISteamStoreService
{
    Task<SteamAppDetailsDto?> GetOrCacheAppDetailsAsync(int appId);
    Task<SteamReviewSummaryDto?> GetReviewSummaryAsync(int appId);
    Task<List<SteamStoreSearchItemDto>> SearchStoreAsync(string query);
    Task<string?> GetCommunityIconUrlAsync(int appId);
}
