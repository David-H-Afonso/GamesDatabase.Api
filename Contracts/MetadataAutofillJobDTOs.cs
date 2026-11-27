namespace GamesDatabase.Api.Contracts;

public sealed class MetadataAutofillStartRequest
{
    public Guid ClientRequestId { get; set; }
    public List<int> GameIds { get; set; } = [];
    public bool Overwrite { get; set; }
    /// <summary>One explicit batch decision; a listed-platform rating is never relabelled Steam-edition.</summary>
    public bool AcceptListedPlatformCritic { get; set; }
}

public class MetadataAutofillItemDto
{
    public int GameId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public List<string> AppliedFields { get; set; } = [];
    public List<string> UnchangedFields { get; set; } = [];
    public List<string> Notices { get; set; } = [];
    public List<GameMetadataIdentityDto> Matches { get; set; } = [];
}

public sealed class MetadataAutofillJobDto
{
    public Guid Id { get; set; }
    public Guid ClientRequestId { get; set; }
    public string State { get; set; } = "queued";
    public bool Overwrite { get; set; }
    public bool AcceptListedPlatformCritic { get; set; }
    public bool Active { get; set; }
    public bool Dismissed { get; set; }
    public long Revision { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public int TotalGames { get; set; }
    public int ProcessedGames { get; set; }
    public int UpdatedGames { get; set; }
    public int UnchangedGames { get; set; }
    public int ReviewGames { get; set; }
    public int FailedGames { get; set; }
    public int PartialGames { get; set; }
    public int CancelledGames { get; set; }
    public int? CurrentGameId { get; set; }
    public string? CurrentGameName { get; set; }
    public string? Phase { get; set; }
    public List<MetadataAutofillItemDto> Items { get; set; } = [];
}
