using System.Text.Json;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IGameViewService
{
    Task<IEnumerable<GameViewSummaryDto>> GetViewsAsync(int userId);
    Task<GameViewDto?> GetViewByIdAsync(int id, int userId);
    Task<GameViewDto?> GetViewByNameAsync(string name, int userId);
    Task<CatalogServiceResult<GameViewDto>> CreateViewAsync(GameViewCreateDto dto, int userId);
    Task<CatalogServiceResult<GameViewDto>> UpdateViewAsync(int id, GameViewUpdateDto dto, int userId);
    Task<CatalogServiceResult> ReorderViewsAsync(ReorderStatusesDto dto, int userId);
    Task<CatalogServiceResult> DeleteViewAsync(int id, int userId);
    Task<CatalogServiceResult<GameViewDto>> DuplicateViewAsync(int id, string newName, int userId);
    Task<CatalogServiceResult<GameViewDto>> UpdateViewConfigurationAsync(int id, JsonElement body, int userId);
}
