namespace GamesDatabase.Api.Authentication;

public static class HouseholdAccessTokenDefaults
{
    public const string AuthenticationScheme = "HouseholdAccessToken";
    public const string ConnectionIdClaim = "household_connection_id";
    public const string IntegrationClaim = "household_integration";
    public const string ScopeClaim = "scope";
}
