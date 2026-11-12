namespace GamesDatabase.Api.Contracts.Steam;

public sealed record SteamSyncProgress
{
    public int TotalGames { get; init; }
    public int ProcessedGames { get; init; }
    public int GamesUpdated { get; init; }
    public int AchievementsUpdated { get; init; }
    public int FailedGames { get; init; }
    public int WarningCount { get; init; }
    public int InfoCount { get; init; }
    public IReadOnlyDictionary<string, int> ReasonCounts { get; init; } = new Dictionary<string, int>();
    public SteamSyncIssueDto? Issue { get; init; }
    public string? CurrentGameName { get; init; }
}

public sealed class SteamSyncJobDto
{
    public Guid Id { get; set; }
    public string State { get; set; } = "queued";
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int TotalGames { get; set; }
    public int ProcessedGames { get; set; }
    public int GamesUpdated { get; set; }
    public int AchievementsUpdated { get; set; }
    public int FailedGames { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public Dictionary<string, int> ReasonCounts { get; set; } = new();
    public List<SteamSyncIssueDto> Issues { get; set; } = [];
    public bool IssuesTruncated { get; set; }
    public string? CurrentGameName { get; set; }
    public string? Error { get; set; }
    public SteamSyncResult? Result { get; set; }
}

public sealed record SteamSyncJobStartResult(SteamSyncJobDto? Job, string? Error = null);
public sealed record SteamSyncCurrentJobResponse(SteamSyncJobDto? Job);

public sealed record SteamSyncIssueDto(int GameId, int SteamAppId, string GameName, string Severity, string Code);
