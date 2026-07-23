namespace GamesDatabase.Api.Configuration;

public sealed class HouseholdIntegrationOptions
{
    public const string SectionName = "HouseholdIntegration";

    public string ClientId { get; set; } = "household";
    public string RedirectUris { get; set; } = string.Empty;
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
    public int AuthorizationCodeMinutes { get; set; } = 5;

    public IReadOnlySet<string> GetRedirectUris() => RedirectUris
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.Ordinal);
}
