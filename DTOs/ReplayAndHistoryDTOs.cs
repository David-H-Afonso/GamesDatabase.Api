namespace GamesDatabase.Api.DTOs;

// ─── GameReplayType ────────────────────────────────────────────────────────────

public class GameReplayTypeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
    public bool IsDefault { get; set; }
    public string ReplayType { get; set; } = "None";
    public bool IsSpecialType { get; set; }
}

public class GameReplayTypeCreateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "#ffffff";
    public int? SortOrder { get; set; }
}

public class GameReplayTypeUpdateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
    public int? SortOrder { get; set; }
}

public class ReorderReplayTypesDto
{
    public List<int> OrderedIds { get; set; } = new List<int>();
}

// ─── GameReplay ────────────────────────────────────────────────────────────────

public class GameReplayDto
{
    public int Id { get; set; }
    public int GameId { get; set; }
    public int ReplayTypeId { get; set; }
    public string? ReplayTypeName { get; set; }
    public string? ReplayTypeColor { get; set; }
    public string? Started { get; set; }
    public string? Finished { get; set; }
    public int? Grade { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class GameReplayCreateDto
{
    public int GameId { get; set; }
    public int? ReplayTypeId { get; set; } // si null, backend usa el tipo especial Replay
    public string? Started { get; set; }
    public string? Finished { get; set; }
    public int? Grade { get; set; }
    public string? Notes { get; set; }
}

public class GameReplayUpdateDto
{
    public int? ReplayTypeId { get; set; }
    public string? Started { get; set; }
    public string? Finished { get; set; }
    public int? Grade { get; set; }
    public string? Notes { get; set; }
}

// ─── GameHistoryEntry ─────────────────────────────────────────────────────────

public class GameHistoryEntryDto
{
    public int Id { get; set; }
    public int? GameId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ActionType { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
}
