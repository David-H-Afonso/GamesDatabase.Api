namespace GamesDatabase.Api.Application.Services;

public class FolderAnalysisResult
{
    public int TotalGamesInDatabase { get; set; }
    public int TotalFoldersInFilesystem { get; set; }
    public int Difference { get; set; }
    public List<PotentialDuplicate> PotentialDuplicates { get; set; } = new();
    public List<OrphanFolder> OrphanFolders { get; set; } = new();
    public List<MissingGameFolder> MissingGameFolders { get; set; } = new();
    /// <summary>Database duplicate detection results, always populated regardless of filesystem availability.</summary>
    public DatabaseDuplicatesResult DatabaseDuplicates { get; set; } = new();
}

public class PotentialDuplicate
{
    public string GameName { get; set; } = string.Empty;
    public List<string> FolderNames { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

public class OrphanFolder
{
    public string FolderName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
}

public class MissingGameFolder
{
    public int GameId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string ExpectedFolderName { get; set; } = string.Empty;
    public string ExpectedFullPath { get; set; } = string.Empty;
}

// ─── Database Duplicate Detection ────────────────────────────────────────────

public class DatabaseDuplicatesResult
{
    public int TotalGamesInDatabase { get; set; }
    public List<DatabaseDuplicateGroup> DuplicateGroups { get; set; } = new();
}

public class DatabaseDuplicateGroup
{
    /// <summary>The canonical normalized representation used to group the duplicates.</summary>
    public string NormalizedKey { get; set; } = string.Empty;
    public List<DatabaseDuplicateEntry> Games { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
}

public class DatabaseDuplicateEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? StatusName { get; set; }
    public string? PlatformName { get; set; }
    public string? PlayedStatusName { get; set; }
    public string? Released { get; set; }
    public string? Started { get; set; }
    public string? Finished { get; set; }
    public int? Grade { get; set; }
    public int? Critic { get; set; }
    public decimal? Score { get; set; }
    public int? Story { get; set; }
    public int? Completion { get; set; }
    public string? Logo { get; set; }
    public string? Cover { get; set; }
    public int? SteamAppId { get; set; }
    public int? SteamPlaytimeForever { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class DeleteOrphanFolderRequest
{
    public string FolderName { get; set; } = string.Empty;
}
