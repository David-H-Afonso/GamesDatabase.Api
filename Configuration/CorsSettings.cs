namespace GamesDatabase.Api.Configuration;

public class CorsSettings
{
    public const string SectionName = "CorsSettings";

    public List<string> AllowedOrigins { get; set; } = new();
}