using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Models;

public enum SpecialStatusType
{
    None = 0,
    NotFulfilled = 1
    // Future special statuses can be added here
}

public class GameStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "#ffffff";

    /// <summary>
    /// Indicates if this status is a special status that cannot be deleted directly
    /// </summary>
    public bool IsDefault { get; set; } = false;

    /// <summary>
    /// The type of special status (None for regular statuses)
    /// </summary>
    public SpecialStatusType StatusType { get; set; } = SpecialStatusType.None;

    // Navigation property - ignorada en JSON
    [JsonIgnore]
    public virtual ICollection<Game> Games { get; set; } = new List<Game>();

    /// <summary>
    /// Checks if this status is a special status that requires reassignment before deletion
    /// </summary>
    public bool IsSpecialStatus => StatusType != SpecialStatusType.None;
}