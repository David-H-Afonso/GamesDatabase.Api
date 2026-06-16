namespace GamesDatabase.Api.Domain.Entities;

/// <summary>
/// Represents a long-lived refresh token that allows issuing new short-lived
/// access tokens without requiring the user to log in again.
/// The raw token is a 64-char hex string (32 random bytes) and is only
/// returned once (at login/refresh time). Only its value is stored here.
/// </summary>
public class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>64-char hex string (32 cryptographically random bytes).</summary>
    public string Token { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Revoked { get; set; }
    public DateTime? RevokedAt { get; set; }
}
