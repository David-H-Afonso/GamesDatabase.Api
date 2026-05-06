using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Models;

public class GameReplay
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int ReplayTypeId { get; set; }
    public string? Started { get; set; }
    public string? Finished { get; set; }
    public int? Grade { get; set; }
    public string? Notes { get; set; }

    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore]
    public virtual Game Game { get; set; } = null!;
    [JsonIgnore]
    public virtual GameReplayType ReplayType { get; set; } = null!;
    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}
