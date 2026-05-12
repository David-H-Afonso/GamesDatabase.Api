namespace GamesDatabase.Api.DTOs;

public class GameDto
{
    public int Id { get; set; }
    public int StatusId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Grade { get; set; }
    public int? Critic { get; set; }
    public string? CriticProvider { get; set; }
    public int? Story { get; set; }
    public int? Completion { get; set; }
    public decimal? Score { get; set; }
    public int? PlatformId { get; set; }
    public string? Released { get; set; }
    public string? Started { get; set; }
    public string? Finished { get; set; }
    public string? Comment { get; set; }
    public List<int> PlayWithIds { get; set; } = new List<int>();
    public int? PlayedStatusId { get; set; }
    public string? Logo { get; set; }
    public string? Cover { get; set; }

    // Price comparison fields
    public bool? IsCheaperByKey { get; set; }
    public string? KeyStoreUrl { get; set; }

    // Steam integration
    public int? SteamAppId { get; set; }
    public int? SteamPlaytimeForever { get; set; }
    public int? SteamPlaytime2Weeks { get; set; }
    public DateTime? SteamLastSynced { get; set; }
    public int SteamAchievementsUnlocked { get; set; }
    public int SteamAchievementsTotal { get; set; }
    public string? SteamFinishedSource { get; set; }
    public string? SteamFinishedLastValue { get; set; }
    public DateTime? SteamFinishedSyncedAt { get; set; }
    public string? SteamFinishedRejectedValue { get; set; }

    // Manual 100% completion
    public bool IsManuallyCompleted { get; set; }

    // Audit fields
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties simplificadas (solo datos básicos)
    public string? StatusName { get; set; }
    public string? PlatformName { get; set; }
    public List<string> PlayWithNames { get; set; } = new List<string>();
    public string? PlayedStatusName { get; set; }
}

public class GameCreateDto
{
    public int StatusId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Grade { get; set; }
    public int? Critic { get; set; }
    public string? CriticProvider { get; set; }
    public int? Story { get; set; }
    public int? Completion { get; set; }
    public int? PlatformId { get; set; }
    public string? Released { get; set; }
    public string? Started { get; set; }
    public string? Finished { get; set; }
    public string? Comment { get; set; }
    public List<int> PlayWithIds { get; set; } = new List<int>();
    public int? PlayedStatusId { get; set; }
    public string? Logo { get; set; }
    public string? Cover { get; set; }

    // Price comparison fields
    public bool? IsCheaperByKey { get; set; }
    public string? KeyStoreUrl { get; set; }

    // Steam integration
    public int? SteamAppId { get; set; }

    // Manual 100% completion
    public bool? IsManuallyCompleted { get; set; }
}

public class GameUpdateDto
{
    public int? StatusId { get; set; }
    public string? Name { get; set; }
    public int? Grade { get; set; }
    public int? Critic { get; set; }
    public string? CriticProvider { get; set; }
    public int? Story { get; set; }
    public int? Completion { get; set; }

    public int? PlatformId { get; set; }
    public string? Released { get; set; }
    public string? Started { get; set; }
    public string? Finished { get; set; }
    public string? Comment { get; set; }
    public List<int>? PlayWithIds { get; set; }
    public int? PlayedStatusId { get; set; }
    public string? Logo { get; set; }
    public string? Cover { get; set; }

    // Price comparison fields
    public bool? IsCheaperByKey { get; set; }
    public string? KeyStoreUrl { get; set; }

    // Steam integration
    public int? SteamAppId { get; set; }

    // Manual 100% completion
    public bool? IsManuallyCompleted { get; set; }
}

public class BulkUpdateGameDto
{
    public int[]? GameIds { get; set; }
    public int? StatusId { get; set; }
    public int? PlatformId { get; set; }
    public int[]? PlayWithIds { get; set; }
    public int? PlayedStatusId { get; set; }
    public bool? IsCheaperByKey { get; set; }
}

public class BulkUpdateResult
{
    public int UpdatedCount { get; set; }
    public int TotalRequested { get; set; }
}
