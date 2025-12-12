using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Models;

public class Game
{
    public int Id { get; set; }
    public int StatusId { get; set; }
    public string Name { get; set; } = string.Empty;

    [Range(0, 100)]
    public int? Grade { get; set; }

    [Range(0, 100)]
    public int? Critic { get; set; }

    public string? CriticProvider { get; set; }

    [Range(0, 100)]
    public int? Story { get; set; }

    [Range(0, 100)]
    public int? Completion { get; set; }

    [Range(0, 100)]
    public decimal? Score { get; private set; }

    public int? PlatformId { get; set; }
    public string? Released { get; set; }
    public string? Started { get; set; }
    public string? Finished { get; set; }
    public string? Comment { get; set; }
    public int? PlayedStatusId { get; set; }

    // Image paths for logo and cover
    public string? Logo { get; set; }
    public string? Cover { get; set; }

    // Price comparison fields
    public bool? IsCheaperByKey { get; set; }
    public string? KeyStoreUrl { get; set; }

    // Export tracking
    public bool ModifiedSinceExport { get; set; } = true;

    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [JsonIgnore]
    public virtual User User { get; set; } = null!;
    [JsonIgnore]
    public virtual GameStatus Status { get; set; } = null!;
    [JsonIgnore]
    public virtual GamePlatform? Platform { get; set; }
    [JsonIgnore]
    public virtual GamePlayedStatus? PlayedStatus { get; set; }

    // Relación muchos-a-muchos con PlayWith
    [JsonIgnore]
    public virtual ICollection<GamePlayWithMapping> GamePlayWiths { get; set; } = new List<GamePlayWithMapping>();

    /// <summary>
    /// Calcula el score automáticamente basado en la fórmula: 10 * (Critic / 100) * (10 / (Story + 10))
    /// </summary>
    public void CalculateScore()
    {
        if (Critic.HasValue && Story.HasValue)
        {
            // Fórmula: 10 * (Critic / 100) * (10 / (Story + 10))
            var scoreValue = 10.0m * (Critic.Value / 100.0m) * (10.0m / (Story.Value + 10.0m));
            Score = Math.Round(scoreValue, 2);
        }
        else
        {
            Score = null;
        }
    }
}