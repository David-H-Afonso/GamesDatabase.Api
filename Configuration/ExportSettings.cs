namespace GamesDatabase.Api.Configuration;

public class ExportSettings
{
    public const string SectionName = "ExportSettings";

    public string DefaultExportPath { get; set; } = "../exports";
    public string CsvDelimiter { get; set; } = ",";
    public string CsvEncoding { get; set; } = "UTF-8";
}