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
}

public sealed class PlaylistUpdateDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? HeroUrl { get; set; }
    public string? CoverUrl { get; set; }
    public string? LogoUrl { get; set; }
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
