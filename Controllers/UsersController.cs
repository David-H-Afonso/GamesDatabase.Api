using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.Services;
using GamesDatabase.Api.Helpers;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly GamesDbContext _context;
    private readonly IAuthService _authService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(GamesDbContext context, IAuthService authService, ILogger<UsersController> logger)
    {
        _context = context;
        _authService = authService;
        _logger = logger;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
    {
        var response = await _authService.AuthenticateAsync(request.Username, request.Password);

        if (response == null)
            return Unauthorized(new { message = "Invalid username or password" });

        return Ok(response);
    }
    [HttpGet]
    [Authorize]
    [Produces("application/json")]
    public async Task<ActionResult<List<UserDto>>> GetUsers()
    {
        var currentUserId = HttpContext.GetUserId();
        if (!currentUserId.HasValue)
            return Unauthorized(new { message = "User ID required" });

        var currentUser = await _context.Users.FindAsync(currentUserId.Value);
        if (currentUser == null)
            return NotFound(new { message = "Current user not found" });

        if (currentUser.Role != UserRole.Admin)
            return StatusCode(403, new { message = "Admin access required" });

        var users = await _context.Users
            .OrderBy(u => u.Username)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Username = u.Username,
                Role = u.Role.ToString(),
                IsDefault = u.IsDefault,
                HasPassword = u.PasswordHash != null,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id}")]
    [Authorize]
    [Produces("application/json")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var currentUserId = HttpContext.GetUserId();
        if (!currentUserId.HasValue)
            return Unauthorized(new { message = "User ID required" });

        var currentUser = await _context.Users.FindAsync(currentUserId.Value);
        if (currentUser == null)
            return NotFound(new { message = "Current user not found" });

        if (currentUser.Role != UserRole.Admin && currentUser.Id != id)
            return StatusCode(403, new { message = "Access denied. You can only view your own profile." });

        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound(new { message = $"User with ID {id} not found" });

        return Ok(new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Role = user.Role.ToString(),
            IsDefault = user.IsDefault,
            HasPassword = user.PasswordHash != null,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        });
    }

    [HttpPost]
    [Authorize]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<ActionResult<UserDto>> CreateUser([FromBody] CreateUserRequest request)
    {
        var currentUserId = HttpContext.GetUserId();
        if (!currentUserId.HasValue)
            return Unauthorized(new { message = "User ID required" });

        var currentUser = await _context.Users.FindAsync(currentUserId.Value);
        if (currentUser == null || currentUser.Role != UserRole.Admin)
            return Forbid();

        if (await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower()))
            return BadRequest(new { message = "Username already exists" });

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role))
            return BadRequest(new { message = "Invalid role. Must be 'Admin' or 'Standard'" });

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

        return CreatedAtAction(nameof(GetUser), new { id = newUser.Id }, new UserDto
        {
            Id = newUser.Id,
            Username = newUser.Username,
            Role = newUser.Role.ToString(),
            IsDefault = newUser.IsDefault,
            HasPassword = newUser.PasswordHash != null,
            CreatedAt = newUser.CreatedAt,
            UpdatedAt = newUser.UpdatedAt
        });
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
    {
        var currentUserId = HttpContext.GetUserId();
        if (!currentUserId.HasValue)
            return Unauthorized(new { message = "User ID required" });

        var currentUser = await _context.Users.FindAsync(currentUserId.Value);
        if (currentUser == null || currentUser.Role != UserRole.Admin)
            return Forbid();

        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound(new { message = $"User with ID {id} not found" });

        if (request.Username != null)
        {
            if (await _context.Users.AnyAsync(u => u.Id != id && u.Username.ToLower() == request.Username.ToLower()))
                return BadRequest(new { message = "Username already exists" });

            user.Username = request.Username;
        }

        if (request.Role != null)
        {
            if (!Enum.TryParse<UserRole>(request.Role, true, out var newRole))
                return BadRequest(new { message = "Invalid role. Must be 'Admin' or 'Standard'" });

            if (user.Role == UserRole.Admin && newRole != UserRole.Admin)
            {
                var adminCount = await _context.Users.CountAsync(u => u.Role == UserRole.Admin);
                if (adminCount <= 1)
                    return BadRequest(new { message = "Cannot change role. At least one admin must exist" });
            }

            user.Role = newRole;
        }

        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var currentUserId = HttpContext.GetUserId();
        if (!currentUserId.HasValue)
            return Unauthorized(new { message = "User ID required" });

        var currentUser = await _context.Users.FindAsync(currentUserId.Value);
        if (currentUser == null || currentUser.Role != UserRole.Admin)
            return Forbid();

        var user = await _context.Users.FindAsync(id);
        if (user == null)
            return NotFound(new { message = $"User with ID {id} not found" });

        if (user.IsDefault)
            return BadRequest(new { message = "Cannot delete default admin user" });

        if (user.Role == UserRole.Admin)
        {
            var adminCount = await _context.Users.CountAsync(u => u.Role == UserRole.Admin);
            if (adminCount <= 1)
                return BadRequest(new { message = "Cannot delete last admin user" });
        }

        _context.Users.Remove(user);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id}/password")]
    [Authorize]
    [Consumes("application/json")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordRequest request)
    {
        var currentUserId = HttpContext.GetUserId();
        if (!currentUserId.HasValue)
            return Unauthorized(new { message = "User ID required" });

        var currentUser = await _context.Users.FindAsync(currentUserId.Value);
        if (currentUser == null)
            return NotFound();

        var targetUser = await _context.Users.FindAsync(id);
        if (targetUser == null)
            return NotFound(new { message = $"User with ID {id} not found" });

        if (currentUser.Role != UserRole.Admin && currentUser.Id != id)
            return Forbid();

        if (currentUser.Role == UserRole.Standard && targetUser.Role == UserRole.Admin && targetUser.Id != currentUser.Id)
            return Forbid();

        targetUser.PasswordHash = !string.IsNullOrEmpty(request.NewPassword)
            ? _authService.HashPassword(request.NewPassword)
            : null;

        await _context.SaveChangesAsync();

        return NoContent();
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

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Health check endpoint for Docker/Kubernetes
    /// </summary>
    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<IActionResult> HealthCheck()
    {
        try
        {
            // Verificar que la base de datos esté accesible
            await _context.Database.CanConnectAsync();
            return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(503, new { status = "unhealthy", error = ex.Message });
        }
    }
}
