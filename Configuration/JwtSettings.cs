namespace GamesDatabase.Api.Configuration;

public class JwtSettings
{
    public const string SectionName = "JwtSettings";

    public string SecretKey { get; set; } = string.Empty;
    public string Issuer { get; set; } = "GamesDatabase.Api";
    public string Audience { get; set; } = "GamesDatabase.Client";

    /// <summary>Access-token lifetime in minutes. Default: 10080 (7 days).</summary>
    public int ExpirationMinutes { get; set; } = 10080;

    /// <summary>Refresh-token lifetime in days. Default: 365 (1 year max session).</summary>
    public int RefreshTokenExpirationDays { get; set; } = 365;
}
