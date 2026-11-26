namespace GamesDatabase.Api.Contracts;

public sealed class HomeSourceDto
{
    public string Kind { get; set; } = "playlist";
    public int? PlaylistId { get; set; }
    public int? ViewId { get; set; }
    public PlaylistRulesDto? Rules { get; set; }
    public RecommendationFiltersDto? Recommendations { get; set; }
}

public sealed class HomeRowDto
{
    public string Id { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string? Title { get; set; }
    public HomeSourceDto Source { get; set; } = new();
    public string Presentation { get; set; } = "covers";
    public int PreviewCount { get; set; } = 12;
    public bool HighlightFirst { get; set; } = true;
}

public sealed class HomeConfigDto
{
    public int Version { get; set; } = 1;
    public int Revision { get; set; }
    public List<HomeRowDto> Rows { get; set; } = [];
}

public sealed class HomeResolveRequest
{
    public List<HomeRowDto> Rows { get; set; } = [];
}

public sealed class HomeRowResultDto
{
    public string RowId { get; set; } = string.Empty;
    public string State { get; set; } = "ready";
    public string? SourceName { get; set; }
    public string? ErrorCode { get; set; }
    public int TotalMatches { get; set; }
    public int ReturnedCount { get; set; }
    public bool HasMore => TotalMatches > ReturnedCount;
    public List<GameDto> Items { get; set; } = [];
    public CollectionTotalsDto? Summary { get; set; }
    public string? RecommendationDay { get; set; }
    public DateTimeOffset? RefreshAt { get; set; }
    public DateTimeOffset? ServerTime { get; set; }
}

public sealed class GotySelectionRequest
{
    public int Revision { get; set; }
    public int? GameId { get; set; }
}

public sealed class GotySelectionsDto
{
    public int Revision { get; set; }
    public Dictionary<int, int> Selections { get; set; } = [];
}

public sealed class CollectionTotalsDto
{
    public int TotalGames { get; set; }
    public long EffectiveMinutes { get; set; }
    public long SteamMinutes { get; set; }
    public long ManualMinutes { get; set; }
    public int TimedGames { get; set; }
    public int MissingTimeGames { get; set; }
    public int CompletedGames { get; set; }
    public int FullyCompletedGames { get; set; }
    public int ClosureGames { get; set; }
    public int MissingFinishGames { get; set; }
    public int MissingStoryGames { get; set; }
    public int AchievementsUnlocked { get; set; }
    public int AchievementsTotal { get; set; }
    public int AchievementGames { get; set; }
    public int UnknownAchievementDates { get; set; }
}

public sealed record CollectionStatusCountDto(int StatusId, string Name, string Color, int Count);
public sealed record CollectionMonthDto(int Month, int Records, int Games, int MainRecords, int ReplayRecords, int Unlocks, GameDto? ArtworkGame = null);
public sealed record CollectionProgressBinDto(int Minimum, string Label, int Count);
public sealed record CollectionUnlockGameDto(GameDto Game, int Count);
public sealed record CollectionDeveloperDto(string Name, int Count, int RatedCount, double? Average, List<GameDto> Games);
public sealed record CollectionActivityDto(string Id, string Source, int RecordId, GameDto Game, string Date, int? Grade, string Type);

public sealed class CollectionStatisticsDto
{
    public int Version { get; set; } = 1;
    public int Year { get; set; }
    public int? Month { get; set; }
    public string DateKind { get; set; } = "finished";
    public string TimeBasis { get; set; } = "accumulated-only";
    public string DateBasis { get; set; } = "stored-records-not-sessions";
    public List<int> Years { get; set; } = [];
    public CollectionTotalsDto Totals { get; set; } = new();
    public List<CollectionStatusCountDto> ByStatus { get; set; } = [];
    public List<CollectionMonthDto> Months { get; set; } = [];
    public List<CollectionActivityDto> Records { get; set; } = [];
    public List<CollectionActivityDto> Ranking { get; set; } = [];
    public int DistinctGames { get; set; }
    public int MainRecords { get; set; }
    public int ReplayRecords { get; set; }
    public int RatedGames { get; set; }
    public double? Average { get; set; }
    public int DatedUnlocks { get; set; }
    public List<CollectionUnlockGameDto> UnlocksByGame { get; set; } = [];
    public List<CollectionProgressBinDto> ProgressBins { get; set; } = [];
    public List<GameDto> NearComplete { get; set; } = [];
    public List<GameDto> TopPlaytime { get; set; } = [];
    public List<CollectionDeveloperDto> Developers { get; set; } = [];
}
