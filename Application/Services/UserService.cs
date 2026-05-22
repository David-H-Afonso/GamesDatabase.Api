using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;

namespace GamesDatabase.Api.Application.Services;

public class UserService : IUserService
{
    private readonly GamesDbContext _context;
    private readonly IAuthService _authService;
    private readonly ILogger<UserService> _logger;

    public UserService(GamesDbContext context, IAuthService authService, ILogger<UserService> logger)
    {
        _context = context;
        _authService = authService;
        _logger = logger;
    }

    public async Task<List<UserDto>> GetUsersAsync(int currentUserId)
    {
        var users = await _context.Users
            .OrderBy(u => u.Username)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                Role = u.Role.ToString(),
                IsDefault = u.IsDefault,
                HasPassword = u.PasswordHash != null,
                UseScoreColors = u.UseScoreColors,
                ScoreProvider = u.ScoreProvider,
                ShowPriceComparisonIcon = u.ShowPriceComparisonIcon,
                SteamId = u.SteamId,
                SteamNickname = u.SteamNickname,
                SteamAvatarUrl = u.SteamAvatarUrl,
                SteamLinkedAt = u.SteamLinkedAt,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt
            })
            .ToListAsync();

        return users;
    }

    public async Task<CatalogServiceResult<UserDto>> GetUserByIdAsync(int id, int currentUserId)
    {
        var currentUser = await _context.Users.FindAsync(currentUserId);
        if (currentUser == null)
            return CatalogServiceResult<UserDto>.NotFoundResult("Current user not found");

        if (currentUser.Role != UserRole.Admin && currentUser.Id != id)
            return new CatalogServiceResult<UserDto> { StatusCode = 403, Error = "Access denied. You can only view your own profile." };

        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return CatalogServiceResult<UserDto>.NotFoundResult($"User with ID {id} not found");

        return CatalogServiceResult<UserDto>.Ok(new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Role = user.Role.ToString(),
            IsDefault = user.IsDefault,
            HasPassword = user.PasswordHash != null,
            UseScoreColors = user.UseScoreColors,
            ScoreProvider = user.ScoreProvider,
            ShowPriceComparisonIcon = user.ShowPriceComparisonIcon,
            SteamId = user.SteamId,
            SteamNickname = user.SteamNickname,
            SteamAvatarUrl = user.SteamAvatarUrl,
            SteamLinkedAt = user.SteamLinkedAt,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        });
    }

    public async Task<CatalogServiceResult<UserDto>> CreateUserAsync(CreateUserRequest request, int currentUserId)
    {
        var currentUser = await _context.Users.FindAsync(currentUserId);
        if (currentUser == null || currentUser.Role != UserRole.Admin)
            return new CatalogServiceResult<UserDto> { StatusCode = 403, Error = "Admin access required" };

        if (await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower()))
            return CatalogServiceResult<UserDto>.BadRequest("Username already exists");

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            return CatalogServiceResult<UserDto>.BadRequest("Invalid role. Must be 'Admin' or 'Standard'");

        var newUser = new User
        {
            Username = request.Username,
            Role = role,
            PasswordHash = !string.IsNullOrEmpty(request.Password)
                ? _authService.HashPassword(request.Password)
                : null
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync();

        await SeedUserDefaultDataAsync(newUser.Id);

        return CatalogServiceResult<UserDto>.Ok(new UserDto
        {
            Id = newUser.Id,
            Username = newUser.Username,
            Role = newUser.Role.ToString(),
            IsDefault = newUser.IsDefault,
            HasPassword = newUser.PasswordHash != null,
            UseScoreColors = newUser.UseScoreColors,
            ScoreProvider = newUser.ScoreProvider,
            ShowPriceComparisonIcon = newUser.ShowPriceComparisonIcon,
            CreatedAt = newUser.CreatedAt,
            UpdatedAt = newUser.UpdatedAt
        });
    }

    public async Task<CatalogServiceResult> UpdateUserAsync(int id, UpdateUserRequest request, int currentUserId)
    {
        var currentUser = await _context.Users.FindAsync(currentUserId);
        if (currentUser == null)
            return CatalogServiceResult.NotFoundResult("Current user not found");

        if (currentUser.Role != UserRole.Admin && currentUser.Id != id)
            return new CatalogServiceResult { StatusCode = 403, Error = "Access denied" };

        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return CatalogServiceResult.NotFoundResult($"User with ID {id} not found");

        if (currentUser.Role == UserRole.Admin)
        {
            if (request.Username != null)
            {
                if (await _context.Users.AnyAsync(u => u.Id != id && u.Username.ToLower() == request.Username.ToLower()))
                    return CatalogServiceResult.BadRequest("Username already exists");

                user.Username = request.Username;
            }

            if (request.Role != null)
            {
                if (!Enum.TryParse<UserRole>(request.Role, true, out var newRole))
                    return CatalogServiceResult.BadRequest("Invalid role. Must be 'Admin' or 'Standard'");

                if (user.Role == UserRole.Admin && newRole != UserRole.Admin)
                {
                    var adminCount = await _context.Users.CountAsync(u => u.Role == UserRole.Admin);
                    if (adminCount <= 1)
                        return CatalogServiceResult.BadRequest("Cannot change role. At least one admin must exist");
                }

                user.Role = newRole;
            }
        }

        if (request.UseScoreColors.HasValue)
        {
            user.UseScoreColors = request.UseScoreColors.Value;
        }

        if (request.ScoreProvider != null)
        {
            user.ScoreProvider = request.ScoreProvider;
        }

        if (request.ShowPriceComparisonIcon.HasValue)
        {
            user.ShowPriceComparisonIcon = request.ShowPriceComparisonIcon.Value;
        }

        await _context.SaveChangesAsync();

        return CatalogServiceResult.Ok();
    }

    public async Task<CatalogServiceResult> DeleteUserAsync(int id, int currentUserId)
    {
        var currentUser = await _context.Users.FindAsync(currentUserId);
        if (currentUser == null || currentUser.Role != UserRole.Admin)
            return new CatalogServiceResult { StatusCode = 403, Error = "Admin access required" };

        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return CatalogServiceResult.NotFoundResult($"User with ID {id} not found");

        if (user.IsDefault)
            return CatalogServiceResult.BadRequest("Cannot delete default admin user");

        if (user.Role == UserRole.Admin)
        {
            var adminCount = await _context.Users.CountAsync(u => u.Role == UserRole.Admin);
            if (adminCount <= 1)
                return CatalogServiceResult.BadRequest("Cannot delete last admin user");
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        return CatalogServiceResult.Ok();
    }

    public async Task<CatalogServiceResult> ChangePasswordAsync(int id, ChangePasswordRequest request, int currentUserId)
    {
        var currentUser = await _context.Users.FindAsync(currentUserId);
        if (currentUser == null)
            return CatalogServiceResult.NotFoundResult("Current user not found");

        var targetUser = await _context.Users.FindAsync(id);
        if (targetUser == null)
            return CatalogServiceResult.NotFoundResult($"User with ID {id} not found");

        if (currentUser.Role != UserRole.Admin && currentUser.Id != id)
            return new CatalogServiceResult { StatusCode = 403, Error = "Access denied" };

        if (currentUser.Role == UserRole.Standard && targetUser.Role == UserRole.Admin && targetUser.Id != currentUser.Id)
            return new CatalogServiceResult { StatusCode = 403, Error = "Access denied" };

        targetUser.PasswordHash = !string.IsNullOrEmpty(request.NewPassword)
            ? _authService.HashPassword(request.NewPassword)
            : null;

        await _context.SaveChangesAsync();

        return CatalogServiceResult.Ok();
    }

    public async Task<bool> HealthCheckAsync()
    {
        return await _context.Database.CanConnectAsync();
    }

    private async Task SeedUserDefaultDataAsync(int userId)
    {
        var platforms = new List<GamePlatform>
        {
            new() { Name = "Steam", Color = "#2a475e", SortOrder = 1, UserId = userId },
            new() { Name = "Epic Games", Color = "#2F2D2E", SortOrder = 2, UserId = userId },
            new() { Name = "GOG", Color = "#c99aff", SortOrder = 3, UserId = userId },
            new() { Name = "Itch.io", Color = "#de4660", SortOrder = 4, UserId = userId },
            new() { Name = "EA", Color = "#EA2020", SortOrder = 5, UserId = userId },
            new() { Name = "Ubisoft", Color = "#1472F1", SortOrder = 6, UserId = userId },
            new() { Name = "Battle.net", Color = "#009AE4", SortOrder = 7, UserId = userId },
            new() { Name = "Emulator", Color = "#d12e2e", SortOrder = 8, UserId = userId },
            new() { Name = "Nintendo Switch", Color = "#fe0016", SortOrder = 9, UserId = userId }
        };

        var statuses = new List<GameStatus>
        {
            new() { Name = "Pending", Color = "#be9c23", SortOrder = 1, UserId = userId },
            new() { Name = "Next up", Color = "#793e77", SortOrder = 2, UserId = userId },
            new() { Name = "DEFAULT_PLAYING", Color = "#61afef", SortOrder = 3, IsDefault = true, StatusType = SpecialStatusType.Playing, UserId = userId },
            new() { Name = "Done", Color = "#3fc20f", SortOrder = 4, UserId = userId },
            new() { Name = "Abandoned", Color = "#b91d1d", SortOrder = 5, UserId = userId },
            new() { Name = "DEFAULT_NOT_FULFILLED", Color = "#919191", SortOrder = 6, IsDefault = true, StatusType = SpecialStatusType.NotFulfilled, UserId = userId }
        };

        var playWiths = new List<GamePlayWith>
        {
            new() { Name = "Solo", Color = "#24c2b7", SortOrder = 1, UserId = userId },
            new() { Name = "Friends", Color = "#ab32ec", SortOrder = 2, UserId = userId },
            new() { Name = "Family", Color = "#099012", SortOrder = 3, UserId = userId }
        };

        var playedStatuses = new List<GamePlayedStatus>
        {
            new() { Name = "None", Color = "#b5b5b5", SortOrder = 1, UserId = userId },
            new() { Name = "Some", Color = "#873ed0", SortOrder = 2, UserId = userId },
            new() { Name = "Almost", Color = "#cc1eb5", SortOrder = 3, UserId = userId },
            new() { Name = "Completed", Color = "#2ed42b", SortOrder = 4, UserId = userId },
            new() { Name = "Abandoned", Color = "#a60808", SortOrder = 5, UserId = userId }
        };

        if (!await _context.GamePlatforms.AnyAsync(p => p.UserId == userId))
        {
            _context.GamePlatforms.AddRange(platforms);
        }

        if (!await _context.GameStatuses.AnyAsync(s => s.UserId == userId))
        {
            _context.GameStatuses.AddRange(statuses);
        }

        if (!await _context.GamePlayWiths.AnyAsync(pw => pw.UserId == userId))
        {
            _context.GamePlayWiths.AddRange(playWiths);
        }

        if (!await _context.GamePlayedStatuses.AnyAsync(ps => ps.UserId == userId))
        {
            _context.GamePlayedStatuses.AddRange(playedStatuses);
        }

        if (!await _context.GameReplayTypes.AnyAsync(rt => rt.UserId == userId))
        {
            _context.GameReplayTypes.AddRange(
                new GameReplayType { Name = "Rejugado", Color = "#61afef", SortOrder = 1, IsDefault = true, ReplayType = SpecialReplayType.Replay, UserId = userId },
                new GameReplayType { Name = "DLC", Color = "#c678dd", SortOrder = 2, UserId = userId },
                new GameReplayType { Name = "Expansión", Color = "#98c379", SortOrder = 3, UserId = userId },
                new GameReplayType { Name = "NG+", Color = "#e5c07b", SortOrder = 4, UserId = userId },
                new GameReplayType { Name = "100%", Color = "#e06c75", SortOrder = 5, UserId = userId },
                new GameReplayType { Name = "Logros", Color = "#56b6c2", SortOrder = 6, UserId = userId }
            );
        }

        await _context.SaveChangesAsync();
    }
}
