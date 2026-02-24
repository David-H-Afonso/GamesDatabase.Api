namespace GamesDatabase.Api.Services;

public class FolderAnalysisResult
{
    public int TotalGamesInDatabase { get; set; }
    public int TotalFoldersInFilesystem { get; set; }
    public int Difference { get; set; }
    public List<PotentialDuplicate> PotentialDuplicates { get; set; } = new();
    public List<OrphanFolder> OrphanFolders { get; set; } = new();
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
}
