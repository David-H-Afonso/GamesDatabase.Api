using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> AuthenticateAsync(string username, string? password);
    string GenerateToken(User user);
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}
