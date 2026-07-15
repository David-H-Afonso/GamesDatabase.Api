namespace GamesDatabase.Api.Common;

public class QueryParameters
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = false;
    public bool? IsActive { get; set; }

    public int Skip => (Page - 1) * PageSize;
    public int Take => PageSize;
}

public class GameQueryParameters : QueryParameters
{
    public int? StatusId { get; set; }
    public int[]? ExcludeStatusIds { get; set; }
    public int? PlatformId { get; set; }
    public int? PlayWithId { get; set; }
    public int? PlayedStatusId { get; set; }
    public int? MinGrade { get; set; }
    public int? MaxGrade { get; set; }
    public string? Released { get; set; }
    public int? ReleasedYear { get; set; }
    public string? Started { get; set; }
    public int? StartedYear { get; set; }
    public string? Finished { get; set; }
    public int? FinishedYear { get; set; }
    public bool? IncludeReplayDates { get; set; }

    /// <summary>
    /// Filter by games cheaper by key. true = only cheaper by key, false = only cheaper in official store, null = all
    /// </summary>
    public bool? IsCheaperByKey { get; set; }

    /// <summary>
    /// Filter incomplete games: not fulfilled, no hero, no logo, or no platform
    /// </summary>
    public bool? ShowIncomplete { get; set; }

    /// <summary>
    /// Filter by critic provider. null = all, "Default" = only without specific provider, or provider name
    /// </summary>
    public string? CriticProvider { get; set; }

    /// <summary>
    /// View ID to apply. If specified, other filters and sorting will be ignored
    /// </summary>
    public int? ViewId { get; set; }

    /// <summary>
    /// View name to apply. If specified, other filters and sorting will be ignored
    /// </summary>
    public string? ViewName { get; set; }

    // ─── Replay filters ─────────────────────────────────────────────────

    public string? ReplayStartedFrom { get; set; }
    public string? ReplayStartedTo { get; set; }
    public string? ReplayFinishedFrom { get; set; }
    public string? ReplayFinishedTo { get; set; }
    public int? ReplayTypeId { get; set; }
    public int? ReplayGradeMin { get; set; }
    public int? ReplayGradeMax { get; set; }
    public bool? HasReplays { get; set; }
    public bool? HasSteamApp { get; set; }
    public bool? FullCompletion { get; set; }
    public string? MissingDuration { get; set; }

    /// <summary>
    /// "any" (default): alguna rejugada cumple el filtro.
    /// "all": todas las rejugadas deben cumplir el filtro.
    /// </summary>
    public string? ReplayMatchMode { get; set; }
}

public class PagedResult<T>
{
    public IEnumerable<T> Data { get; set; } = new List<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}
