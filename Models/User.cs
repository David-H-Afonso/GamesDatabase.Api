using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Models;

public enum UserRole
{
    Standard = 0,
    Admin = 1
}

public class User
{
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;

    [JsonIgnore]
    public string? PasswordHash { get; set; }

    [Required]
    public UserRole Role { get; set; } = UserRole.Standard;

    public bool IsDefault { get; set; } = false;

    // User preferences
    public bool UseScoreColors { get; set; } = false;
    public string ScoreProvider { get; set; } = "Metacritic";
    public bool ShowPriceComparisonIcon { get; set; } = false;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore]
    public virtual ICollection<Game> Games { get; set; } = new List<Game>();
    [JsonIgnore]
    public virtual ICollection<GamePlatform> Platforms { get; set; } = new List<GamePlatform>();
    [JsonIgnore]
    public virtual ICollection<GameStatus> Statuses { get; set; } = new List<GameStatus>();
    [JsonIgnore]
    public virtual ICollection<GamePlayWith> PlayWiths { get; set; } = new List<GamePlayWith>();
    [JsonIgnore]
    public virtual ICollection<GamePlayedStatus> PlayedStatuses { get; set; } = new List<GamePlayedStatus>();
    [JsonIgnore]
    public virtual ICollection<GameView> Views { get; set; } = new List<GameView>();
}
