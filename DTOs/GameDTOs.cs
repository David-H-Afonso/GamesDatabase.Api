namespace GamesDatabase.Api.DTOs;

public class GameDto
{
    public int Id { get; set; }
    public int StatusId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? Grade { get; set; }
    public int? Critic { get; set; }
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
}

public class GameUpdateDto
{
    public int? StatusId { get; set; }
    public string? Name { get; set; }
    public int? Grade { get; set; }
    public int? Critic { get; set; }
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

}