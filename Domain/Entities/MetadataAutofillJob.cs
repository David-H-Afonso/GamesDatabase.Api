using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Domain.Entities;

/// <summary>Durable, explicitly requested metadata work. No tokens or provider credentials.</summary>
public sealed class MetadataAutofillJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public Guid ClientRequestId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public int? ActiveUserId { get; set; }
    public bool Overwrite { get; set; }
    public bool AcceptListedPlatformCritic { get; set; }
    public string State { get; set; } = "queued";
    public string ItemsJson { get; set; } = "[]";
    public string? CurrentPhase { get; set; }
    public int? CurrentGameId { get; set; }
    public bool CancelRequested { get; set; }
    public bool Dismissed { get; set; }
    public long Revision { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? LeaseOwner { get; set; }
    public DateTime? LeaseUntil { get; set; }
    [JsonIgnore]
    public User User { get; set; } = null!;
}
