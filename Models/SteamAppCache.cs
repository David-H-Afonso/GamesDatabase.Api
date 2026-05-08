namespace GamesDatabase.Api.Models;

public class SteamAppCache
{
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public string? GenresJson { get; set; }
    public string? CategoriesJson { get; set; }
    public string? ReleaseDate { get; set; }
    public int? MetacriticScore { get; set; }
    public string? HeaderImageUrl { get; set; }
    public string? BackgroundImageUrl { get; set; }
    public string? Price { get; set; }
    public bool IsFree { get; set; }
    public DateTime LastFetched { get; set; }
}
