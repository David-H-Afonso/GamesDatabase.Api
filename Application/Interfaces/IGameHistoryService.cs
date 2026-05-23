using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IGameHistoryService
{
    Task RecordCreatedAsync(Game game, int userId);
    Task RecordUpdatedAsync(Game before, System.Text.Json.JsonElement patch, int userId,
        string? oldStatusName, string? newStatusName,
        string? oldPlatformName, string? newPlatformName,
        string? oldPlayedStatusName, string? newPlayedStatusName);
    Task RecordDeletedAsync(Game game, int userId);
    Task<PagedResult<GameHistoryEntryDto>> GetGameHistoryAsync(int userId, int gameId, int page, int pageSize);
    Task<PagedResult<GameHistoryEntryDto>> GetAllHistoryAsync(int userId, int page, int pageSize,
        string? actionType, string? field, int? gameId, DateTime? from, DateTime? to, string? search = null);
    Task<bool> DeleteHistoryEntryAsync(int userId, int gameId, int entryId);
    Task<bool> DeleteAllGameHistoryAsync(int userId, int gameId);
    Task<PagedResult<GameHistoryEntryDto>?> GetAdminHistoryAsync(int currentUserId, int page, int pageSize,
        int? userId, string? actionType, DateTime? from, DateTime? to, string? field = null, string? search = null);
}
