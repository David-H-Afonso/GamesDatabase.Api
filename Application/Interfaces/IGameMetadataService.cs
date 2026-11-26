using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IGameMetadataService
{
    Task<CatalogServiceResult<GameMetadataPreviewDto>> PreviewAsync(int gameId, int userId, GameMetadataLookupRequest request, CancellationToken cancellationToken = default);
    Task<CatalogServiceResult<GameMetadataApplyDto>> ApplyAsync(int gameId, int userId, GameMetadataApplyRequest request, CancellationToken cancellationToken = default);
}
