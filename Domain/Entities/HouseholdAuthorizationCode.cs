namespace GamesDatabase.Api.Domain.Entities;

public sealed class HouseholdAuthorizationCode
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public HouseholdConnection Connection { get; set; } = null!;
    public string CodeHash { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string CodeChallenge { get; set; } = string.Empty;
    public string GrantedScopes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ConsumedAt { get; set; }
}
