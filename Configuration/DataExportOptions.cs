namespace GamesDatabase.Api.Configuration;

public class DataExportOptions
{
    public const string SectionName = "DataExport";

    public string FullExportUrl { get; set; } = string.Empty;
}
