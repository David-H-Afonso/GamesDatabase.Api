namespace GamesDatabase.Api.Configuration;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "GamesDatabase.Api";
    public string Audience { get; set; } = "GamesDatabase.Client";
    public int ExpirationMinutes { get; set; } = 525600; // 1 year
}
