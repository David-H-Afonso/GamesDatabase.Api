using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> AuthenticateAsync(string username, string? password);
    string GenerateToken(User user);

    /// <summary>
    /// Generates a cryptographically random refresh token, persists it in the
    /// database, and returns the raw token value to be sent to the client.
    /// </summary>
    Task<string> GenerateAndStoreRefreshTokenAsync(int userId);

    /// <summary>
    /// Validates a refresh token, rotates it (revokes old + issues new), and
    /// returns a fresh access token + the new refresh token. Returns null if
    /// the token is invalid, expired, or already revoked.
    /// </summary>
    Task<(string AccessToken, string RefreshToken)?> RefreshAccessTokenAsync(string refreshToken);

    /// <summary>Revokes a refresh token so it can no longer be used.</summary>
    Task RevokeRefreshTokenAsync(string refreshToken);

    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}
