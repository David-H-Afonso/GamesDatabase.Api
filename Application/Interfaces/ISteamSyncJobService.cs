using GamesDatabase.Api.Contracts.Steam;

namespace GamesDatabase.Api.Application.Interfaces;

public interface ISteamSyncJobService
{
    Task<SteamSyncJobStartResult> StartAsync(int userId, CancellationToken cancellationToken = default);
    SteamSyncJobDto? GetCurrent(int userId);
    SteamSyncJobDto? GetById(int userId, Guid jobId);
    bool IsActive(int userId);
}
