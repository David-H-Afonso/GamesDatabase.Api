using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Application.Services;

public sealed partial class HouseholdIntegrationService : IHouseholdIntegrationService
{
    public static readonly IReadOnlyList<string> AllowedScopes =
        new[] { "profile.read", "games.read", "games.status.write" };

    private readonly GamesDbContext _context;
    private readonly HouseholdIntegrationOptions _options;

    public HouseholdIntegrationService(
        GamesDbContext context,
        IOptions<HouseholdIntegrationOptions> options)
    {
        _context = context;
        _options = options.Value;
    }

    public async Task<HouseholdAuthorizeResult> AuthorizeAsync(int userId, HouseholdAuthorizeRequest request)
    {
        var redirectIsRegistered = IsRegisteredRedirect(request.RedirectUri);
        if (!redirectIsRegistered)
        {
            return AuthorizationError("invalid_request", "The redirect URI is not registered.");
        }

        if (!string.Equals(request.ClientId, _options.ClientId, StringComparison.Ordinal))
        {
            return RedirectAuthorizationError(request, "unauthorized_client", "Unknown client.");
        }

        if (string.IsNullOrWhiteSpace(request.State))
        {
            return RedirectAuthorizationError(request, "invalid_request", "State is required.");
        }

        if (!string.Equals(request.CodeChallengeMethod, "S256", StringComparison.Ordinal) ||
            !CodeChallengeRegex().IsMatch(request.CodeChallenge))
        {
            return RedirectAuthorizationError(request, "invalid_request", "PKCE S256 is required.");
        }

        var scopes = NormalizeScopes(request.Scopes);
        if (scopes is null)
        {
            return RedirectAuthorizationError(request, "invalid_scope", "One or more requested scopes are not allowed.");
        }

        if (!request.Approved)
        {
            return new HouseholdAuthorizeResult(
                true,
                BuildRedirect(request.RedirectUri, new Dictionary<string, string?>
                {
                    ["error"] = "access_denied",
                    ["state"] = request.State
                }),
                CanRedirect: true);
        }

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(item => item.Id == userId);
        if (user is null)
        {
            return AuthorizationError("invalid_request", "The source account is unavailable.");
        }

        var now = DateTime.UtcNow;
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var connection = await _context.HouseholdConnections
            .FirstOrDefaultAsync(item => item.UserId == userId && item.ClientId == request.ClientId);

        if (connection is null)
        {
            connection = new HouseholdConnection
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ClientId = request.ClientId,
                AccountId = GenerateOpaqueAccountId(),
                CreatedAt = now
            };
            _context.HouseholdConnections.Add(connection);
        }
        else
        {
            await RevokeConnectionCredentialsAsync(connection.Id, now);
        }

        connection.GrantedScopes = scopes;
        connection.Status = HouseholdConnectionStatus.Active;
        connection.UpdatedAt = now;
        connection.RevokedAt = null;

        var rawCode = GenerateToken("gdc_");
        _context.HouseholdAuthorizationCodes.Add(new HouseholdAuthorizationCode
        {
            Id = Guid.NewGuid(),
            Connection = connection,
            CodeHash = HashToken(rawCode),
            RedirectUri = request.RedirectUri,
            CodeChallenge = request.CodeChallenge,
            GrantedScopes = scopes,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.AuthorizationCodeMinutes)
        });

        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return new HouseholdAuthorizeResult(
            true,
            BuildRedirect(request.RedirectUri, new Dictionary<string, string?>
            {
                ["code"] = rawCode,
                ["state"] = request.State
            }),
            CanRedirect: true);
    }

    public Task<HouseholdTokenResult> ExchangeTokenAsync(HouseholdTokenRequest request) =>
        request.GrantType switch
        {
            "authorization_code" => ExchangeAuthorizationCodeAsync(request),
            "refresh_token" => RotateRefreshTokenAsync(request),
            _ => Task.FromResult(TokenError("unsupported_grant_type", "Unsupported grant type."))
        };

    public async Task RevokeAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 512)
        {
            return;
        }

        var tokenHash = HashToken(token);
        var connectionId = await _context.HouseholdAccessTokens
            .Where(item => item.TokenHash == tokenHash)
            .Select(item => (Guid?)item.ConnectionId)
            .FirstOrDefaultAsync();

        connectionId ??= await _context.HouseholdRefreshTokens
            .Where(item => item.TokenHash == tokenHash)
            .Select(item => (Guid?)item.ConnectionId)
            .FirstOrDefaultAsync();

        if (!connectionId.HasValue)
        {
            return;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var connection = await _context.HouseholdConnections.FirstOrDefaultAsync(item => item.Id == connectionId.Value);
        if (connection is not null)
        {
            var now = DateTime.UtcNow;
            connection.Status = HouseholdConnectionStatus.Revoked;
            connection.RevokedAt = now;
            connection.UpdatedAt = now;
            await RevokeConnectionCredentialsAsync(connection.Id, now);
            await _context.SaveChangesAsync();
        }
        await transaction.CommitAsync();
    }

    private async Task<HouseholdTokenResult> ExchangeAuthorizationCodeAsync(HouseholdTokenRequest request)
    {
        if (!string.Equals(request.ClientId, _options.ClientId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.Code) ||
            string.IsNullOrWhiteSpace(request.CodeVerifier) ||
            string.IsNullOrWhiteSpace(request.RedirectUri) ||
            !CodeVerifierRegex().IsMatch(request.CodeVerifier))
        {
            return TokenError("invalid_request", "A valid code, redirect URI, and PKCE verifier are required.");
        }

        var now = DateTime.UtcNow;
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var codeHash = HashToken(request.Code);
        var code = await _context.HouseholdAuthorizationCodes
            .Include(item => item.Connection)
                .ThenInclude(connection => connection.User)
            .FirstOrDefaultAsync(item => item.CodeHash == codeHash);

        if (code is null || code.ConsumedAt.HasValue || code.ExpiresAt <= now ||
            code.Connection.Status != HouseholdConnectionStatus.Active ||
            !string.Equals(code.Connection.ClientId, request.ClientId, StringComparison.Ordinal) ||
            !string.Equals(code.RedirectUri, request.RedirectUri, StringComparison.Ordinal) ||
            !VerifyPkce(request.CodeVerifier, code.CodeChallenge))
        {
            return TokenError("invalid_grant", "The authorization code is invalid, expired, or already used.");
        }

        code.ConsumedAt = now;
        code.Connection.GrantedScopes = code.GrantedScopes;
        code.Connection.LastUsedAt = now;
        code.Connection.UpdatedAt = now;

        var familyId = Guid.NewGuid();
        var pair = AddTokenPair(code.Connection, familyId, now);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return new HouseholdTokenResult(true, BuildTokenResponse(code.Connection, pair));
    }

    private async Task<HouseholdTokenResult> RotateRefreshTokenAsync(HouseholdTokenRequest request)
    {
        if (!string.Equals(request.ClientId, _options.ClientId, StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return TokenError("invalid_request", "A refresh token and valid client ID are required.");
        }

        var now = DateTime.UtcNow;
        await using var transaction = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var refreshHash = HashToken(request.RefreshToken);
        var current = await _context.HouseholdRefreshTokens
            .Include(item => item.Connection)
                .ThenInclude(connection => connection.User)
            .FirstOrDefaultAsync(item => item.TokenHash == refreshHash);

        if (current is null || !string.Equals(current.Connection.ClientId, request.ClientId, StringComparison.Ordinal))
        {
            return TokenError("invalid_grant", "The refresh token is invalid.");
        }

        if (current.RevokedAt.HasValue || current.ReplacedByTokenId.HasValue)
        {
            await RevokeFamilyAsync(current.ConnectionId, current.FamilyId, now);
            await transaction.CommitAsync();
            return TokenError("invalid_grant", "Refresh token reuse was detected; this token family was revoked.");
        }

        if (current.ExpiresAt <= now || current.Connection.Status != HouseholdConnectionStatus.Active)
        {
            current.RevokedAt = now;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            return TokenError("invalid_grant", "The refresh token is expired or revoked.");
        }

        var pair = AddTokenPair(current.Connection, current.FamilyId, now);
        await _context.SaveChangesAsync();

        current.RevokedAt = now;
        current.ReplacedByTokenId = pair.RefreshEntity.Id;
        current.Connection.LastUsedAt = now;
        current.Connection.UpdatedAt = now;
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return new HouseholdTokenResult(true, BuildTokenResponse(current.Connection, pair));
    }

    private TokenPair AddTokenPair(HouseholdConnection connection, Guid familyId, DateTime now)
    {
        var rawAccessToken = GenerateToken("gdi_");
        var rawRefreshToken = GenerateToken("gdr_");
        var refreshEntity = new HouseholdRefreshToken
        {
            Id = Guid.NewGuid(),
            Connection = connection,
            FamilyId = familyId,
            TokenHash = HashToken(rawRefreshToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(_options.RefreshTokenDays)
        };

        _context.HouseholdAccessTokens.Add(new HouseholdAccessToken
        {
            Id = Guid.NewGuid(),
            Connection = connection,
            FamilyId = familyId,
            TokenHash = HashToken(rawAccessToken),
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(_options.AccessTokenMinutes)
        });
        _context.HouseholdRefreshTokens.Add(refreshEntity);

        return new TokenPair(rawAccessToken, rawRefreshToken, refreshEntity);
    }

    private HouseholdTokenResponse BuildTokenResponse(HouseholdConnection connection, TokenPair pair) => new()
    {
        AccessToken = pair.AccessToken,
        ExpiresIn = checked(_options.AccessTokenMinutes * 60),
        RefreshToken = pair.RefreshToken,
        RefreshExpiresIn = checked(_options.RefreshTokenDays * 24 * 60 * 60),
        Scope = connection.GrantedScopes,
        ConnectionId = connection.Id,
        Account = new HouseholdAccountDto
        {
            Id = connection.AccountId,
            DisplayName = connection.User.Username
        }
    };

    private async Task RevokeConnectionCredentialsAsync(Guid connectionId, DateTime now)
    {
        await _context.HouseholdAuthorizationCodes
            .Where(item => item.ConnectionId == connectionId && item.ConsumedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.ConsumedAt, now));
        await _context.HouseholdAccessTokens
            .Where(item => item.ConnectionId == connectionId && item.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RevokedAt, now));
        await _context.HouseholdRefreshTokens
            .Where(item => item.ConnectionId == connectionId && item.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RevokedAt, now));
    }

    private async Task RevokeFamilyAsync(Guid connectionId, Guid familyId, DateTime now)
    {
        await _context.HouseholdAccessTokens
            .Where(item => item.ConnectionId == connectionId && item.FamilyId == familyId && item.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RevokedAt, now));
        await _context.HouseholdRefreshTokens
            .Where(item => item.ConnectionId == connectionId && item.FamilyId == familyId && item.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.RevokedAt, now));
    }

    private bool IsRegisteredRedirect(string redirectUri) =>
        Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
        string.IsNullOrEmpty(uri.Fragment) &&
        _options.GetRedirectUris().Contains(redirectUri);

    private static string? NormalizeScopes(IEnumerable<string> requestedScopes)
    {
        var requested = requestedScopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (requested.Count == 0 || requested.Any(scope => !AllowedScopes.Contains(scope, StringComparer.Ordinal)))
        {
            return null;
        }

        return string.Join(' ', AllowedScopes.Where(requested.Contains));
    }

    private HouseholdAuthorizeResult RedirectAuthorizationError(
        HouseholdAuthorizeRequest request,
        string error,
        string description) => new(
            false,
            BuildRedirect(request.RedirectUri, new Dictionary<string, string?>
            {
                ["error"] = error,
                ["error_description"] = description,
                ["state"] = request.State
            }),
            error,
            description,
            true);

    private static HouseholdAuthorizeResult AuthorizationError(string error, string description) =>
        new(false, Error: error, ErrorDescription: description);

    private static HouseholdTokenResult TokenError(string error, string description) =>
        new(false, Error: error, ErrorDescription: description);

    private static string BuildRedirect(string redirectUri, Dictionary<string, string?> values) =>
        QueryHelpers.AddQueryString(redirectUri, values);

    private static bool VerifyPkce(string verifier, string expectedChallenge)
    {
        var actual = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(actual),
            Encoding.ASCII.GetBytes(expectedChallenge));
    }

    public static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string GenerateToken(string prefix) =>
        prefix + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private static string GenerateOpaqueAccountId() =>
        "gdb_" + WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18));

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodeChallengeRegex();

    [GeneratedRegex("^[A-Za-z0-9._~-]{43,128}$", RegexOptions.CultureInvariant)]
    private static partial Regex CodeVerifierRegex();

    private sealed record TokenPair(
        string AccessToken,
        string RefreshToken,
        HouseholdRefreshToken RefreshEntity);
}
