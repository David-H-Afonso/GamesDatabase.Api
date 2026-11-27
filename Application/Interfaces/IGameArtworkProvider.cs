namespace GamesDatabase.Api.Application.Interfaces;

public sealed class GameArtworkLookup
{
    public List<PublicGameMetadata> Sources { get; set; } = [];
    public bool Unavailable { get; set; }
    public int? RetryAfterSeconds { get; set; }
    public List<Contracts.GameMetadataIdentityDto> Matches { get; set; } = [];
}

public interface IGameArtworkProvider
{
    Task<GameArtworkLookup> LookupAsync(IReadOnlyList<PublicGameMetadata> editionSources, IReadOnlyCollection<string> fields, CancellationToken cancellationToken, bool forceRefresh = false, int? selectedLaunchBoxId = null);
}
