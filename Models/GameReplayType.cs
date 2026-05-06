using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Models;

public enum SpecialReplayType
{
    None = 0,
    Replay = 1
}

public class GameReplayType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "#ffffff";
    public bool IsDefault { get; set; } = false;
    public SpecialReplayType ReplayType { get; set; } = SpecialReplayType.None;

    public int UserId { get; set; }

    [JsonIgnore]
    public virtual User User { get; set; } = null!;
    [JsonIgnore]
    public virtual ICollection<GameReplay> Replays { get; set; } = new List<GameReplay>();

    public bool IsSpecialType => ReplayType != SpecialReplayType.None;
}
