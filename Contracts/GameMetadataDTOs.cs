using System.Text.Json;

namespace GamesDatabase.Api.Contracts;

public sealed class GameMetadataLookupRequest
{
    public bool Overwrite { get; set; }
    public bool IncludeArtwork { get; set; } = true;
    public bool IncludeDurations { get; set; } = true;
    public bool IncludeCritic { get; set; } = true;
    /// <summary>Version 2 explains critic provenance and requests a fresh replacement lookup.</summary>
    public int ReviewVersion { get; set; } = 1;
    public int? SteamAppId { get; set; }
    public string? WikidataId { get; set; }
    public string? DekuDealsSlug { get; set; }
    public int? LaunchBoxId { get; set; }
    public bool BestMatch { get; set; }
}
public sealed class GameMetadataIdentityDto
{
    public string Source { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int? SteamAppId { get; set; }
    public string? WikidataId { get; set; }
    public string? DekuDealsSlug { get; set; }
    public int? LaunchBoxId { get; set; }
    public int? ReleaseYear { get; set; }
    public List<string> Platforms { get; set; } = [];
}
public sealed class GameMetadataFieldDto
{
    public string Field { get; set; } = string.Empty;
    public JsonElement Value { get; set; }
    public JsonElement Current { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public decimal? SourceHours { get; set; }
    public string? CriticProvider { get; set; }
    public string? CurrentCriticProvider { get; set; }
    public string? CriticUrl { get; set; }
    public string? CriticScope { get; set; }
    public bool Unchanged { get; set; }
}
public sealed class GameMetadataPreviewDto
{
    public bool Overwrite { get; set; }
    public int ReviewVersion { get; set; } = 1;
    public string? Token { get; set; }
    public List<GameMetadataIdentityDto> Matches { get; set; } = [];
    public List<GameMetadataFieldDto> Fields { get; set; } = [];
    public List<string> Notices { get; set; } = [];
    public int? RetryAfterSeconds { get; set; }
}
public sealed class GameMetadataApplyRequest
{
    public string Token { get; set; } = string.Empty;
    public List<string> Fields { get; set; } = [];
}
public sealed class GameMetadataApplyDto
{
    public GameDto Game { get; set; } = new();
    public List<string> AppliedFields { get; set; } = [];
    public List<string> UnchangedFields { get; set; } = [];
}
