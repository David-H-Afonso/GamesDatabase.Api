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
    /// <summary>Human-readable explanation of why the folder is considered orphan.</summary>
    public string Reason { get; set; } = string.Empty;
    /// <summary>Folder creation time (UTC), when the filesystem exposes it.</summary>
    public DateTime? CreatedAt { get; set; }
    /// <summary>Folder last-write time (UTC), when the filesystem exposes it.</summary>
    public DateTime? ModifiedAt { get; set; }
    /// <summary>Total size in bytes of the folder contents, when it can be computed.</summary>
    public long? SizeBytes { get; set; }
    /// <summary>Number of files contained in the folder (recursive), when available.</summary>
    public int? FileCount { get; set; }
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
    public string MatchType { get; set; } = "exact";
    public int Confidence { get; set; } = 100;
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
    public string? Hero { get; set; }
    public string? Cover { get; set; }
    public int? SteamAppId { get; set; }
    public int? SteamPlaytimeForever { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Folder / file relationships ──────────────────────────────────────────
    /// <summary>Expected safe folder name for this game in the export structure.</summary>
    public string? FolderName { get; set; }
    /// <summary>Full path of the expected export folder, when the network path is configured.</summary>
    public string? FolderPath { get; set; }
    /// <summary>Whether the export folder physically exists on disk.</summary>
    public bool FolderExists { get; set; }
    /// <summary>Whether the filesystem could actually be inspected (network path reachable).</summary>
    public bool FilesystemChecked { get; set; }

    // ── Export status ────────────────────────────────────────────────────────
    /// <summary>Whether the game has ever been exported (has an export cache entry).</summary>
    public bool IsExported { get; set; }
    /// <summary>Last time the game was exported, when known.</summary>
    public DateTime? LastExportedAt { get; set; }
    /// <summary>Whether the logo image was successfully exported/downloaded.</summary>
    public bool LogoDownloaded { get; set; }
    /// <summary>Whether the hero image was successfully exported/downloaded.</summary>
    public bool HeroDownloaded { get; set; }
    /// <summary>Whether the vertical cover image was successfully exported/downloaded.</summary>
    public bool CoverDownloaded { get; set; }
    /// <summary>Whether the game has pending changes not yet exported.</summary>
    public bool ModifiedSinceExport { get; set; }
}

public class DeleteOrphanFolderRequest
{
    public string FolderName { get; set; } = string.Empty;
}

public class DismissDuplicateGamesRequest
{
    public List<int> GameIds { get; set; } = new();
}
