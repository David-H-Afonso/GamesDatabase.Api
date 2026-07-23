using System.Text.Json;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IGameService
{
    Task<GameServiceResult<PagedResult<GameDto>>> GetGamesAsync(GameQueryParameters parameters, int userId);
    Task<GameSummaryDto> GetSummaryAsync(int userId);
    Task<GameDto?> GetGameByIdAsync(int id, int userId);
    Task<GameServiceResult<GameDto>> UpdateGameStatusAsync(int id, int statusId, int userId);
    Task<GameServiceResult<GameDto>> CreateGameAsync(GameCreateDto dto, int userId);
    Task<GameServiceResult> UpdateGameAsync(int id, JsonElement body, int userId);
    Task<bool> DeleteGameAsync(int id, int userId);
    Task<GameServiceResult<BulkUpdateResult>> BulkUpdateGamesAsync(BulkUpdateGameDto dto, int userId);
}

public class GameServiceResult
{
    public bool Success { get; set; }
    public bool NotFound { get; set; }
    public string? Error { get; set; }

    public static GameServiceResult Ok() => new() { Success = true };
    public static GameServiceResult NotFoundResult() => new() { NotFound = true };
    public static GameServiceResult BadRequest(string error) => new() { Error = error };
}

public class GameServiceResult<T>
{
    public bool Success { get; set; }
    public bool NotFound { get; set; }
    public string? Error { get; set; }
    public T? Data { get; set; }

    public static GameServiceResult<T> Ok(T data) => new() { Success = true, Data = data };
    public static GameServiceResult<T> NotFoundResult(string? error = null) => new() { NotFound = true, Error = error };
    public static GameServiceResult<T> BadRequest(string error) => new() { Error = error };
}
