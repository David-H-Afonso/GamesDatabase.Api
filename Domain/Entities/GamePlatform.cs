using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Domain.Entities;

public class GamePlatform
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "#ffffff";
    public string? Logo { get; set; }

    public int UserId { get; set; }

    [JsonIgnore]
    public virtual User User { get; set; } = null!;
    [JsonIgnore]
    public virtual ICollection<Game> Games { get; set; } = new List<Game>();
}
