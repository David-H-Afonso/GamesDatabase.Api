namespace GamesDatabase.Api.Domain.Entities;

public sealed class HouseholdRefreshToken
{
    public Guid Id { get; set; }
    public Guid ConnectionId { get; set; }
    public HouseholdConnection Connection { get; set; } = null!;
    public Guid FamilyId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
    public HouseholdRefreshToken? ReplacedByToken { get; set; }
}
