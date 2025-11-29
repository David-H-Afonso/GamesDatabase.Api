namespace GamesDatabase.Api.Services;

public interface INetworkSyncService
{
    Task<NetworkSyncResult> SyncToNetworkAsync(string? authorizationHeader, bool fullSync = false);
}
