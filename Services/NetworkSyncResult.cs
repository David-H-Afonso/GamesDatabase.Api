namespace GamesDatabase.Api.Services;

public class NetworkSyncResult
{
    public bool Success { get; set; }
    public TimeSpan ElapsedTime { get; set; }
    public int TotalGames { get; set; }
    public int GamesSynced { get; set; }
    public int GamesSkipped { get; set; }
    public int ImagesSynced { get; set; }
    public int ImagesRetried { get; set; }
    public int ImagesFailed { get; set; }
    public int FilesWritten { get; set; }
    public string? ErrorMessage { get; set; }
}
