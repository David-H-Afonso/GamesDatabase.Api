using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Domain.Entities;

public class Playlist
{
    public int Id { get; set; }

    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    [MaxLength(2000)]
    public string? HeroUrl { get; set; }

    [MaxLength(2000)]
    public string? CoverUrl { get; set; }

    [MaxLength(2000)]
    public string? LogoUrl { get; set; }

    public bool IsAutomatic { get; set; }
    public string? RulesJson { get; set; }

    public int SortOrder { get; set; }
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool ModifiedSinceExport { get; set; } = true;

    [JsonIgnore]
    public virtual User User { get; set; } = null!;

    [JsonIgnore]
    public virtual ICollection<PlaylistItem> Items { get; set; } = new List<PlaylistItem>();
}
