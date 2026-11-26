using GamesDatabase.Api.Contracts.Steam;

namespace GamesDatabase.Api.Application.Interfaces;

public interface ISteamStoreService
{
    Task<SteamAppDetailsDto?> GetOrCacheAppDetailsAsync(int appId);
    Task<SteamAppDetailsDto?> GetAppDetailsAsync(int appId, CancellationToken cancellationToken = default);
    Task<SteamReviewSummaryDto?> GetReviewSummaryAsync(int appId);
    Task<List<SteamStoreSearchItemDto>> SearchStoreAsync(string query);
    Task<string?> GetCommunityIconUrlAsync(int appId, CancellationToken cancellationToken = default);
    Task<SteamAssetUrlsDto?> GetAssetUrlsAsync(int appId, CancellationToken cancellationToken = default);
    Task<string?> GetLibraryCoverUrlAsync(int appId);
}
