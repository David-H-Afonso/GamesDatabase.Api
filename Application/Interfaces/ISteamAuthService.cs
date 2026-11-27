namespace GamesDatabase.Api.Application.Interfaces;

/// <summary>Data returned by the Steam exchange endpoint.</summary>
public record SteamLoginResult(
    string Token,
    string RefreshToken,
    int UserId,
    string Username,
    string Role,
    string? SteamId,
    string? SteamNickname,
    string? SteamAvatarUrl);

/// <summary>Server-owned OpenID state. Callback parameters must never choose the redirect destination.</summary>
public record SteamAuthNonce(int? UserId, string Mode, string FrontendBaseUrl, string ReturnTo);

public interface ISteamAuthService
{
    string BuildLoginUrl(Guid nonce);
    Task<string?> ValidateCallbackAsync(IQueryCollection queryParams, string expectedReturnTo, CancellationToken cancellationToken = default);
    Guid StoreNonce(int? userId, string mode, string frontendBaseUrl, string callbackUrl);
    SteamAuthNonce? ConsumeNonce(Guid nonce);

    /// <summary>
    /// Stores a short-lived (5 min) one-time code that maps to the full login
    /// result. Used by the Steam callback to avoid putting the JWT in the URL.
    /// </summary>
    Guid StoreLoginResult(SteamLoginResult result);

    /// <summary>
    /// Consumes and returns the login result for a given code.
    /// Returns null if the code is unknown or expired.
    /// </summary>
    SteamLoginResult? ConsumeLoginResult(Guid code);
}
