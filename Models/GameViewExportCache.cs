using System.ComponentModel.DataAnnotations;

namespace GamesDatabase.Api.Models;

/// <summary>
/// Tracks when each game view was last exported to detect changes
/// </summary>
public class GameViewExportCache
{
    [Key]
    public int Id { get; set; }

    public int GameViewId { get; set; }

    /// <summary>
    /// When this view was last exported
    /// </summary>
    public DateTime LastExportedAt { get; set; }

    /// <summary>
    /// Hash of the view configuration (FiltersJson + SortingJson + Name)
    /// Used to detect changes
    /// </summary>
    [MaxLength(64)]
    public string ConfigurationHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual GameView GameView { get; set; } = null!;
}
