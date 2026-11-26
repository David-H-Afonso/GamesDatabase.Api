namespace GamesDatabase.Api.Contracts;

public sealed class DisplayedRandomGameRequest
{
    public List<int>? GameIds { get; set; }
}

public sealed class RecommendationFiltersDto
{
    // Empty includes means all regular (non-special) statuses. Explicit includes
    // may opt into a special status; exclusions always take precedence.
    public List<int> IncludedStatusIds { get; set; } = [];
    public List<int> ExcludedStatusIds { get; set; } = [];
}

public sealed class DailyRecommendationsDto
{
    public string Day { get; set; } = string.Empty;
    public DateTimeOffset RefreshAt { get; set; }
    public DateTimeOffset ServerTime { get; set; }
    public int TotalMatches { get; set; }
    public List<GameDto> Items { get; set; } = [];
}
