using System.Text.Json;

namespace GamesDatabase.Api.Contracts;

public sealed class GameMetadataLookupRequest
{
    public bool Overwrite { get; set; }
    public bool IncludeArtwork { get; set; } = true;
    public int? SteamAppId { get; set; }
    public string? WikidataId { get; set; }
}
public sealed class GameMetadataIdentityDto
{
    public string Source { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public int? SteamAppId { get; set; }
    public string? WikidataId { get; set; }
}
public sealed class GameMetadataFieldDto
{
    public string Field { get; set; } = string.Empty;
    public JsonElement Value { get; set; }
    public JsonElement Current { get; set; }
    public string Source { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}
public sealed class GameMetadataPreviewDto
{
    public bool Overwrite { get; set; }
    public string? Token { get; set; }
    public List<GameMetadataIdentityDto> Matches { get; set; } = [];
    public List<GameMetadataFieldDto> Fields { get; set; } = [];
    public List<string> Notices { get; set; } = [];
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
}
