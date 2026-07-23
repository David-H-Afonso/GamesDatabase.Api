namespace GamesDatabase.Api.Domain.Entities;

public enum HouseholdConnectionStatus
{
    Active,
    Revoked
}

public sealed class HouseholdConnection
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public string ClientId { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string GrantedScopes { get; set; } = string.Empty;
    public HouseholdConnectionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }

    public ICollection<HouseholdAuthorizationCode> AuthorizationCodes { get; set; } = new List<HouseholdAuthorizationCode>();
    public ICollection<HouseholdAccessToken> AccessTokens { get; set; } = new List<HouseholdAccessToken>();
    public ICollection<HouseholdRefreshToken> RefreshTokens { get; set; } = new List<HouseholdRefreshToken>();
}
