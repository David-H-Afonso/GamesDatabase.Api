using System.Security.Claims;
using System.Text.Encodings.Web;
using GamesDatabase.Api.Application.Services;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Authentication;

public sealed class HouseholdAccessTokenHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly GamesDbContext _context;

    public HouseholdAccessTokenHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        GamesDbContext context)
        : base(options, logger, encoder)
    {
        _context = context;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        var token = authorization["Bearer ".Length..].Trim();
        if (!token.StartsWith("gdi_", StringComparison.Ordinal) || token.Length > 512)
        {
            return AuthenticateResult.NoResult();
        }

        var tokenHash = HouseholdIntegrationService.HashToken(token);
        var accessToken = await _context.HouseholdAccessTokens
            .AsNoTracking()
            .Include(item => item.Connection)
                .ThenInclude(connection => connection.User)
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash);

        if (accessToken is null || accessToken.RevokedAt.HasValue || accessToken.ExpiresAt <= DateTime.UtcNow ||
            accessToken.Connection.Status != HouseholdConnectionStatus.Active || accessToken.Connection.User is null)
        {
            return AuthenticateResult.Fail("Invalid or expired integration access token.");
        }

        var now = DateTime.UtcNow;
        await _context.HouseholdConnections
            .Where(item => item.Id == accessToken.ConnectionId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.LastUsedAt, now)
                .SetProperty(item => item.UpdatedAt, now));

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, accessToken.Connection.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new(ClaimTypes.Name, accessToken.Connection.User.Username),
            new(HouseholdAccessTokenDefaults.ConnectionIdClaim, accessToken.ConnectionId.ToString()),
            new(HouseholdAccessTokenDefaults.IntegrationClaim, "true")
        };

        claims.AddRange(accessToken.Connection.GrantedScopes
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(scope => new Claim(HouseholdAccessTokenDefaults.ScopeClaim, scope)));

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}
