using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public interface ICollectionService
{
    Task<HomeConfigDto> GetHomeAsync(int userId);
    Task<CatalogServiceResult<HomeConfigDto>> SaveHomeAsync(HomeConfigDto request, int userId);
    Task<CatalogServiceResult<List<HomeRowResultDto>>> ResolveHomeAsync(HomeResolveRequest request, int userId);
    Task<PagedResult<GameDto>?> GetHomeRowGamesAsync(string rowId, int userId, int page, int pageSize, string? search);
    Task<GotySelectionsDto> GetGotyAsync(int userId);
    Task<CatalogServiceResult<GotySelectionsDto>> SaveGotyAsync(int year, GotySelectionRequest request, int userId);
    Task<CollectionStatisticsDto> GetStatisticsAsync(int userId, int year, int? month, string dateKind, bool allRanking);
    Task<PagedResult<GameDto>> GetMetricGamesAsync(int userId, string metric, int year, int? month, int? value, string? developer, int page, int pageSize, string? search);
}
