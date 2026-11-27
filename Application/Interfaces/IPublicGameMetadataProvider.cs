using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public sealed class PublicGameMetadata
{
    public GameMetadataIdentityDto Identity { get; set; } = new();
    public Dictionary<string, object> Fields { get; set; } = [];
    public Dictionary<string, decimal> SourceHours { get; set; } = [];
    public string? CriticProvider { get; set; }
    public string? CriticUrl { get; set; }
    public string? CriticScope { get; set; }
    // Server-side discovery evidence, never accepted as arbitrary client URLs.
    public List<string> OfficialUrls { get; set; } = [];
    public string? CommonsLogo { get; set; }
    public Dictionary<string, string> ArtworkCandidates { get; set; } = [];
    public List<string> Aliases { get; set; } = [];
    public List<string> Notices { get; set; } = [];
}
public interface IPublicGameMetadataProvider
{
    Task<List<GameMetadataIdentityDto>> SearchSteamAsync(string name, CancellationToken cancellationToken);
    Task<PublicGameMetadata?> SteamAsync(int appId, bool artwork, CancellationToken cancellationToken);
    Task<List<PublicGameMetadata>> WikidataAsync(string name, int? appId, CancellationToken cancellationToken, string? selectedId = null, bool forceRefresh = false);
}
