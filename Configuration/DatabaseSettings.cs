namespace GamesDatabase.Api.Configuration;

public class DatabaseSettings
{
    public const string SectionName = "DatabaseSettings";

    public string DatabasePath { get; set; } = "../gamesdatabase.db";
    public bool EnableSensitiveDataLogging { get; set; } = false;
}