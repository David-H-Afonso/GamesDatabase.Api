using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IGameReplayService
{
    Task<List<GameReplayDto>?> GetReplaysForGameAsync(int userId, int gameId);
    Task<GameReplayDto?> GetGameReplayAsync(int userId, int gameId, int id);
    Task<(GameReplayDto? Dto, string? Error)> CreateReplayAsync(int userId, int gameId, GameReplayCreateDto dto);
    Task<(bool Success, string? Error)> UpdateReplayAsync(int userId, int gameId, int id, GameReplayUpdateDto dto);
    Task<bool> DeleteReplayAsync(int userId, int gameId, int id);
}
