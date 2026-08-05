using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IPlaylistService
{
    Task<IEnumerable<PlaylistSummaryDto>> GetPlaylistsAsync(int userId);
    Task<PlaylistDto?> GetPlaylistByIdAsync(int id, int userId);
    Task<CatalogServiceResult<PlaylistDto>> CreatePlaylistAsync(PlaylistCreateDto dto, int userId);
    Task<CatalogServiceResult<PlaylistDto>> UpdatePlaylistAsync(int id, PlaylistUpdateDto dto, int userId);
    Task<CatalogServiceResult> DeletePlaylistAsync(int id, int userId);
    Task<CatalogServiceResult> ReorderPlaylistsAsync(ReorderPlaylistsDto dto, int userId);
    Task<CatalogServiceResult<PlaylistDto>> AddItemAsync(int id, AddPlaylistItemDto dto, int userId);
    Task<CatalogServiceResult<PlaylistDto>> RemoveItemAsync(int id, int itemId, int userId);
    Task<CatalogServiceResult> ReorderItemsAsync(int id, ReorderPlaylistItemsDto dto, int userId);
    Task<CatalogServiceResult<PlaylistTransferDto>> ExportPlaylistAsync(int id, PlaylistExportReference reference, int userId);
    Task<CatalogServiceResult<PlaylistDto>> ImportPlaylistAsync(PlaylistTransferDto dto, int userId);
}
