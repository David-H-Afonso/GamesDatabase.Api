using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Domain.Entities;

public class SteamAchievement
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? GameId { get; set; }
    public int SteamAppId { get; set; }
    public string ApiName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Achieved { get; set; }
    public DateTime? UnlockTime { get; set; }
    public string? IconUrl { get; set; }
    public string? IconGrayUrl { get; set; }
    public bool Hidden { get; set; }
    public long? InternalKey { get; set; }
    public int GroupId { get; set; }
    public bool Archived { get; set; }
    public bool IsActive { get; set; } = true;
    public double? GlobalPercent { get; set; }
    public int ProgressType { get; set; }
    public double? ProgressCurrent { get; set; }
    public double? ProgressMin { get; set; }
    public double? ProgressMax { get; set; }
    public DateTime LastSynced { get; set; }

    [JsonIgnore]
    public virtual User User { get; set; } = null!;
    [JsonIgnore]
    public virtual Game? Game { get; set; }
}
