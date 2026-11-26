namespace GamesDatabase.Api.Domain.Entities;

public class GameExportCache
{
    public const int CurrentMetadataVersion = 2;
    public int Id { get; set; }
    public int GameId { get; set; }
    public int MetadataVersion { get; set; }
    public Game Game { get; set; } = null!;

    public DateTime LastExportedAt { get; set; }
    public bool LogoDownloaded { get; set; }
    public bool HeroDownloaded { get; set; }
    public bool CoverDownloaded { get; set; }
    public string? LogoUrl { get; set; }
    public string? HeroUrl { get; set; }
    public string? CoverUrl { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
