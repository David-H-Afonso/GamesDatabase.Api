using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Models;

public class GamePlayWith
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "#ffffff";

    // Relación muchos-a-muchos con Game
    [JsonIgnore]
    public virtual ICollection<GamePlayWithMapping> GamePlayWiths { get; set; } = new List<GamePlayWithMapping>();
}