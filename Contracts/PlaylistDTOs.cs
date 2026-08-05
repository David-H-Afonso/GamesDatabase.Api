namespace GamesDatabase.Api.Contracts;

public enum PlaylistExportReference
{
    Id,
    Name
}

public sealed class PlaylistDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? HeroUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? HeroUrlOverride { get; set; }
    public string? CoverUrlOverride { get; set; }
    public string? LogoUrlOverride { get; set; }
    public bool IsAutomatic { get; set; }
    public PlaylistRulesDto? Rules { get; set; }
    public int SortOrder { get; set; }
    public int GameCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<PlaylistItemDto> Items { get; set; } = [];
}

public sealed class PlaylistSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? HeroUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? HeroUrlOverride { get; set; }
    public string? CoverUrlOverride { get; set; }
    public string? LogoUrlOverride { get; set; }
    public bool IsAutomatic { get; set; }
    public PlaylistRulesDto? Rules { get; set; }
    public int SortOrder { get; set; }
    public int GameCount { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class PlaylistItemDto
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int Position { get; set; }
    public GameDto Game { get; set; } = null!;
}

public sealed class PlaylistCreateDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? HeroUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsAutomatic { get; set; }
    public PlaylistRulesDto? Rules { get; set; }
}

public sealed class PlaylistUpdateDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? HeroUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? LogoUrl { get; set; }
    public bool? IsAutomatic { get; set; }
    public PlaylistRulesDto? Rules { get; set; }
}

public sealed class PlaylistRulesDto
{
    public string? Search { get; set; }
    public List<int> StatusIds { get; set; } = [];
    public List<int> PlatformIds { get; set; } = [];
    public List<int> PlayedStatusIds { get; set; } = [];
    public bool? Favorite { get; set; }
    public int? MinGrade { get; set; }
    public int? MaxGrade { get; set; }
    public int? MinCritic { get; set; }
    public int? MaxCritic { get; set; }
    public decimal? MinScore { get; set; }
    public decimal? MaxScore { get; set; }
    public int? ReleasedFromYear { get; set; }
    public int? ReleasedToYear { get; set; }
    public bool? HasSteam { get; set; }
    public bool? FullCompletion { get; set; }
    public string SortBy { get; set; } = "Position";
    public bool SortDescending { get; set; }
    public int? Limit { get; set; }
    public List<int> OrderedGameIds { get; set; } = [];
}

public sealed class AddPlaylistItemDto
{
    public int GameId { get; set; }
}

public sealed class ReorderPlaylistItemsDto
{
    public List<int> OrderedIds { get; set; } = [];
}

public sealed class ReorderPlaylistsDto
{
    public List<int> OrderedIds { get; set; } = [];
}

public sealed class PlaylistTransferDto
{
    public string Format { get; set; } = "games-database-playlist";
    public int Version { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? HeroUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? LogoUrl { get; set; }
    public List<PlaylistGameReferenceDto> Games { get; set; } = [];
}

public sealed class PlaylistGameReferenceDto
{
    public int? GameId { get; set; }
    public string? Name { get; set; }
}
