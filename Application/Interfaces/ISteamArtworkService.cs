using GamesDatabase.Api.Contracts.Steam;

namespace GamesDatabase.Api.Application.Interfaces;

public interface ISteamArtworkService
{
    Task<SteamAssetUrlsDto?> GetAsync(int appId, CancellationToken cancellationToken = default);
}
