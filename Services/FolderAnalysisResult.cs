namespace GamesDatabase.Api.Services;

public class FolderAnalysisResult
{
    public int TotalGamesInDatabase { get; set; }
    public int TotalFoldersInFilesystem { get; set; }
    public int Difference { get; set; }
    public List<PotentialDuplicate> PotentialDuplicates { get; set; } = new();
    public List<OrphanFolder> OrphanFolders { get; set; } = new();
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
