using GamesDatabase.Api.Models;

namespace GamesDatabase.Api.Services;

public interface IGameHistoryService
{
    Task RecordCreatedAsync(Game game, int userId);
    Task RecordUpdatedAsync(Game before, System.Text.Json.JsonElement patch, int userId,
        string? oldStatusName, string? newStatusName,
        string? oldPlatformName, string? newPlatformName,
        string? oldPlayedStatusName, string? newPlayedStatusName);
    Task RecordDeletedAsync(Game game, int userId);
}
