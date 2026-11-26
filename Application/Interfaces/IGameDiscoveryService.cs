using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IGameDiscoveryService
{
    Task<GameDto?> RandomAsync(int userId, CancellationToken cancellationToken = default);
    Task<CatalogServiceResult<GameDto>> RandomDisplayedAsync(int userId, List<int>? gameIds, CancellationToken cancellationToken = default);
    Task<string?> ValidateFiltersAsync(RecommendationFiltersDto? filters, int userId, CancellationToken cancellationToken = default);
    Task<DailyRecommendationsDto> DailyAsync(int userId, RecommendationFiltersDto filters, int count, CancellationToken cancellationToken = default);
    Task<PagedResult<GameDto>> DailyPageAsync(int userId, RecommendationFiltersDto filters, int page, int pageSize, string? search, CancellationToken cancellationToken = default);
}
