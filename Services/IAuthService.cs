using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Models;

namespace GamesDatabase.Api.Services;

public interface IAuthService
{
    Task<LoginResponse?> AuthenticateAsync(string username, string? password);
    string GenerateToken(User user);
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}
