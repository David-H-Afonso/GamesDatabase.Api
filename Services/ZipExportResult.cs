namespace GamesDatabase.Api.Services;

public class ZipExportResult
{
    public byte[] ZipBytes { get; set; } = Array.Empty<byte>();
    public TimeSpan ElapsedTime { get; set; }
    public int TotalGames { get; set; }
    public int GamesExported { get; set; }
    public int GamesSkipped { get; set; }
    public int ImagesDownloaded { get; set; }
    public int ImagesRetried { get; set; }
    public int ImagesFailed { get; set; }
}
