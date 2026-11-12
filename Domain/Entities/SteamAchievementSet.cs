using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Domain.Entities;

public class SteamAchievementSet
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int? GameId { get; set; }
    public int SteamAppId { get; set; }
    public int GroupId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int? DlcAppId { get; set; }
    public int SortOrder { get; set; }
    public bool Archived { get; set; }
    public bool DeveloperOnly { get; set; }
    public bool IsPublic { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public int TotalAchievements { get; set; }
    public int CompletionAchievements { get; set; }
    public bool? IsAchievable { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime LastSynced { get; set; }

    [JsonIgnore]
    public virtual User User { get; set; } = null!;
    [JsonIgnore]
    public virtual Game? Game { get; set; }
}
