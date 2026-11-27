using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IMetadataAutofillJobService
{
    Task<CatalogServiceResult<MetadataAutofillJobDto>> StartAsync(int userId, MetadataAutofillStartRequest request, CancellationToken token = default);
    Task<MetadataAutofillJobDto?> CurrentAsync(int userId, bool includeItems = true, CancellationToken token = default);
    Task<List<MetadataAutofillJobDto>> HistoryAsync(int userId, int skip = 0, int take = 10, CancellationToken token = default);
    Task<MetadataAutofillJobDto?> GetAsync(int userId, Guid jobId, bool includeItems = true, CancellationToken token = default);
    Task<CatalogServiceResult<MetadataAutofillJobDto>> CancelAsync(int userId, Guid jobId, CancellationToken token = default);
    Task<CatalogServiceResult<MetadataAutofillJobDto>> RetryAsync(int userId, Guid jobId, CancellationToken token = default);
    Task<CatalogServiceResult<MetadataAutofillJobDto>> DismissAsync(int userId, Guid jobId, CancellationToken token = default);
    Task<CatalogServiceResult<MetadataAutofillJobDto>> AcceptListedCriticsAsync(int userId, Guid jobId, CancellationToken token = default);
    Task<CatalogServiceResult<GameMetadataApplyDto>> ApplyReviewAsync(int userId, Guid jobId, int gameId, GameMetadataApplyRequest request, CancellationToken token = default);
    Task<bool> ProcessNextAsync(string workerId, CancellationToken token = default);
}
