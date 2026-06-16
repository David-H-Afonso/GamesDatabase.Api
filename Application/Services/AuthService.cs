using GamesDatabase.Api.Application.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using BCrypt.Net;

namespace GamesDatabase.Api.Application.Services;

public class AuthService : IAuthService
{
    private readonly GamesDbContext _context;
    private readonly JwtSettings _jwtSettings;

    public AuthService(GamesDbContext context, IOptions<JwtSettings> jwtSettings)
    {
        _context = context;
        _jwtSettings = jwtSettings.Value;
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    public async Task<LoginResponse?> AuthenticateAsync(string username, string? password)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());

        if (user == null)
            return null;

        // Allow login without password for users with null PasswordHash (like default Admin)
        if (user.PasswordHash != null)
        {
            if (string.IsNullOrEmpty(password) || !VerifyPassword(password, user.PasswordHash))
                return null;
        }

        var accessToken = GenerateToken(user);
        var refreshToken = await GenerateAndStoreRefreshTokenAsync(user.Id);

        return new LoginResponse
        {
            UserId = user.Id,
            Username = user.Username,
            Role = user.Role.ToString(),
            Token = accessToken,
            RefreshToken = refreshToken,
            SteamId = user.SteamId,
            SteamNickname = user.SteamNickname,
            SteamAvatarUrl = user.SteamAvatarUrl
        };
    }

    // ── Access-token generation ────────────────────────────────────────────────

    public string GenerateToken(User user)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(_jwtSettings.SecretKey);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationMinutes),
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    // ── Refresh-token CRUD ─────────────────────────────────────────────────────

    public async Task<string> GenerateAndStoreRefreshTokenAsync(int userId)
    {
        // Lazily clean up expired tokens for this user (keep the table lean)
        var cutoff = DateTime.UtcNow;
        var expired = await _context.RefreshTokens
            .Where(rt => rt.UserId == userId && (rt.Revoked || rt.ExpiresAt <= cutoff))
            .ToListAsync();
        if (expired.Count > 0)
            _context.RefreshTokens.RemoveRange(expired);

        var rawToken = GenerateRawToken();
        var entity = new RefreshToken
        {
            UserId = userId,
            Token = rawToken,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow,
            Revoked = false,
        };

        _context.RefreshTokens.Add(entity);
        await _context.SaveChangesAsync();

        return rawToken;
    }

    public async Task<(string AccessToken, string RefreshToken)?> RefreshAccessTokenAsync(string refreshToken)
    {
        var entity = await _context.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken);

        if (entity == null || entity.Revoked || entity.ExpiresAt <= DateTime.UtcNow)
            return null;

        // Rotate: revoke old, issue new
        entity.Revoked = true;
        entity.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        var newAccessToken = GenerateToken(entity.User);
        var newRefreshToken = await GenerateAndStoreRefreshTokenAsync(entity.UserId);

        return (newAccessToken, newRefreshToken);
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken)
    {
        var entity = await _context.RefreshTokens
            .FirstOrDefaultAsync(rt => rt.Token == refreshToken && !rt.Revoked);

        if (entity == null)
            return;

        entity.Revoked = true;
        entity.RevokedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
    }

    // ── Password helpers ───────────────────────────────────────────────────────

    public string HashPassword(string password)
        => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public bool VerifyPassword(string password, string passwordHash)
        => BCrypt.Net.BCrypt.Verify(password, passwordHash);

    // ── Private helpers ────────────────────────────────────────────────────────

    private static string GenerateRawToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLower();
    }
}
