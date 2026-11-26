using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public sealed class PublicGameMetadata
{
    public GameMetadataIdentityDto Identity { get; set; } = new();
    public Dictionary<string, object> Fields { get; set; } = [];
}
public interface IPublicGameMetadataProvider
{
    Task<List<GameMetadataIdentityDto>> SearchSteamAsync(string name, CancellationToken cancellationToken);
    Task<PublicGameMetadata?> SteamAsync(int appId, bool artwork, CancellationToken cancellationToken);
    Task<List<PublicGameMetadata>> WikidataAsync(string name, int? appId, CancellationToken cancellationToken);
}
