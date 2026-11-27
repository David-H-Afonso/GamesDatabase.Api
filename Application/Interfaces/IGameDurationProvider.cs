using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Application.Interfaces;

public sealed class GameDurationLookup
{
    public List<GameMetadataIdentityDto> Matches { get; set; } = [];
    public PublicGameMetadata? Metadata { get; set; }
    public bool Unavailable { get; set; }
}

public interface IGameDurationProvider
{
    Task<GameDurationLookup> LookupAsync(string name, string? selectedSlug, CancellationToken cancellationToken, bool forceRefresh = false);
}
