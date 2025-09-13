using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Models;

public class GamePlatform
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "#ffffff";

    // Navigation property - ignorada en JSON
    [JsonIgnore]
    public virtual ICollection<Game> Games { get; set; } = new List<Game>();
}