namespace GamesDatabase.Api.Configuration;

public sealed class GameArtworkSettings
{
    public const string SectionName = "ArtworkSources";
    // Optional backend-only credential. Nintendo/Wikimedia do not need a key.
    public string? SteamGridDbApiKey { get; set; }
}
