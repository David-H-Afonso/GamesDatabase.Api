using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public interface ICatalogService
{
    // ─── Statuses ──────────────────────────────────────────────────────────────
    Task<PagedResult<GameStatusDto>> GetStatusesAsync(QueryParameters parameters, int userId);
    Task<IEnumerable<GameStatusDto>> GetActiveStatusesAsync(int userId);
    Task<IEnumerable<object>> GetOrderedStatusesAsync(int userId);
    Task<GameStatusDto?> GetStatusByIdAsync(int id, int userId);
    Task<CatalogServiceResult<GameStatusDto>> CreateStatusAsync(GameStatusCreateDto dto, int userId);
    Task<CatalogServiceResult> UpdateStatusAsync(int id, GameStatusUpdateDto dto, int userId);
    Task<CatalogServiceResult> ReorderStatusesAsync(ReorderStatusesDto dto, int userId);
    Task<CatalogServiceResult> DeleteStatusAsync(int id, int userId);
    Task<IEnumerable<SpecialStatusDto>> GetSpecialStatusesAsync(int userId);
    Task<CatalogServiceResult<object>> ReassignSpecialStatusAsync(ReassignDefaultStatusDto dto, int userId);
    Task<CatalogServiceResult<object>> DeleteSpecialStatusAsync(int id, int userId);

    // ─── Platforms ─────────────────────────────────────────────────────────────
    Task<PagedResult<GamePlatformDto>> GetPlatformsAsync(QueryParameters parameters, int userId);
    Task<IEnumerable<GamePlatformDto>> GetActivePlatformsAsync(int userId);
    Task<GamePlatformDto?> GetPlatformByIdAsync(int id, int userId);
    Task<CatalogServiceResult<GamePlatformDto>> CreatePlatformAsync(GamePlatformCreateDto dto, int userId);
    Task<CatalogServiceResult> UpdatePlatformAsync(int id, GamePlatformUpdateDto dto, int userId);
    Task<CatalogServiceResult> ReorderPlatformsAsync(ReorderStatusesDto dto, int userId);
    Task<CatalogServiceResult> DeletePlatformAsync(int id, int userId);

    // ─── PlayWith ──────────────────────────────────────────────────────────────
    Task<PagedResult<GamePlayWithDto>> GetPlayWithsAsync(QueryParameters parameters, int userId);
    Task<IEnumerable<GamePlayWithDto>> GetActivePlayWithsAsync(int userId);
    Task<GamePlayWithDto?> GetPlayWithByIdAsync(int id, int userId);
    Task<CatalogServiceResult<GamePlayWithDto>> CreatePlayWithAsync(GamePlayWithCreateDto dto, int userId);
    Task<CatalogServiceResult> UpdatePlayWithAsync(int id, GamePlayWithUpdateDto dto, int userId);
    Task<CatalogServiceResult> ReorderPlayWithsAsync(ReorderStatusesDto dto, int userId);
    Task<CatalogServiceResult> DeletePlayWithAsync(int id, int userId);

    // ─── PlayedStatuses ────────────────────────────────────────────────────────
    Task<PagedResult<GamePlayedStatusDto>> GetPlayedStatusesAsync(QueryParameters parameters, int userId);
    Task<IEnumerable<GamePlayedStatusDto>> GetActivePlayedStatusesAsync(int userId);
    Task<GamePlayedStatusDto?> GetPlayedStatusByIdAsync(int id, int userId);
    Task<CatalogServiceResult<GamePlayedStatusDto>> CreatePlayedStatusAsync(GamePlayedStatusCreateDto dto, int userId);
    Task<CatalogServiceResult> UpdatePlayedStatusAsync(int id, GamePlayedStatusUpdateDto dto, int userId);
    Task<CatalogServiceResult> ReorderPlayedStatusesAsync(ReorderStatusesDto dto, int userId);
    Task<CatalogServiceResult> DeletePlayedStatusAsync(int id, int userId);

    // ─── ReplayTypes ───────────────────────────────────────────────────────────
    Task<PagedResult<GameReplayTypeDto>> GetReplayTypesAsync(QueryParameters parameters, int userId);
    Task<IEnumerable<GameReplayTypeDto>> GetActiveReplayTypesAsync(int userId);
    Task<GameReplayTypeDto?> GetReplayTypeByIdAsync(int id, int userId);
    Task<CatalogServiceResult<GameReplayTypeDto>> GetOrCreateSpecialReplayTypeAsync(int userId);
    Task<CatalogServiceResult<GameReplayTypeDto>> CreateReplayTypeAsync(GameReplayTypeCreateDto dto, int userId);
    Task<CatalogServiceResult> UpdateReplayTypeAsync(int id, GameReplayTypeUpdateDto dto, int userId);
    Task<CatalogServiceResult> ReorderReplayTypesAsync(ReorderReplayTypesDto dto, int userId);
    Task<CatalogServiceResult> DeleteReplayTypeAsync(int id, int userId);
}

public class CatalogServiceResult
{
    public bool Success { get; set; }
    public bool NotFound { get; set; }
    public bool Conflict { get; set; }
    public string? Error { get; set; }
    public int StatusCode { get; set; }
    public object? ErrorData { get; set; }

    public static CatalogServiceResult Ok() => new() { Success = true };
    public static CatalogServiceResult NotFoundResult(string? error = null) => new() { NotFound = true, Error = error };
    public static CatalogServiceResult ConflictResult(string error) => new() { Conflict = true, Error = error };
    public static CatalogServiceResult BadRequest(string error, object? data = null) => new() { Error = error, ErrorData = data };
    public static CatalogServiceResult ServerError(string error) => new() { StatusCode = 500, Error = error };
}

public class CatalogServiceResult<T>
{
    public bool Success { get; set; }
    public bool NotFound { get; set; }
    public bool Conflict { get; set; }
    public string? Error { get; set; }
    public int StatusCode { get; set; }
    public object? ErrorData { get; set; }
    public T? Data { get; set; }

    public static CatalogServiceResult<T> Ok(T data) => new() { Success = true, Data = data };
    public static CatalogServiceResult<T> NotFoundResult(string? error = null) => new() { NotFound = true, Error = error };
    public static CatalogServiceResult<T> ConflictResult(string error) => new() { Conflict = true, Error = error };
    public static CatalogServiceResult<T> BadRequest(string error, object? data = null) => new() { Error = error, ErrorData = data };
    public static CatalogServiceResult<T> ServerError(string error) => new() { StatusCode = 500, Error = error };
}
