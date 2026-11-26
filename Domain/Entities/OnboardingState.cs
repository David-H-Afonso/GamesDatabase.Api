using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Domain.Entities;

public sealed class OnboardingState
{
    public int UserId { get; set; }
    public int Revision { get; set; }
    public string Step { get; set; } = "welcome";
    public string? CompletedVersion { get; set; }
    public string? SampleBatchId { get; set; }
    public int? SampleStatusId { get; set; }
    public DateTime UpdatedAt { get; set; }
    [JsonIgnore]
    public User User { get; set; } = null!;
}
