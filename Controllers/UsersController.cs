using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Common;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IUserService _userService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IAuthService authService, IUserService userService, ILogger<UsersController> logger)
    {
        _authService = authService;
        _userService = userService;
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

        var result = await _userService.GetUsersAsync(currentUserId.Value);
        return Ok(result);
    }

    [HttpGet("{id}")]
    [Authorize]
    [Produces("application/json")]
    public async Task<ActionResult<UserDto>> GetUser(int id)
    {
        var currentUserId = HttpContext.GetUserId();
        if (!currentUserId.HasValue)
            return Unauthorized(new { message = "User ID required" });

        var result = await _userService.GetUserByIdAsync(id, currentUserId.Value);
        if (result.Success) return Ok(result.Data);
        if (result.NotFound) return NotFound(new { message = result.Error });
        if (result.StatusCode == 403) return StatusCode(403, new { message = result.Error });
        return BadRequest(new { message = result.Error });
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

        var result = await _userService.CreateUserAsync(request, currentUserId.Value);
        if (result.Success) return CreatedAtAction(nameof(GetUser), new { id = result.Data!.Id }, result.Data);
        if (result.StatusCode == 403) return Forbid();
        if (result.Conflict) return BadRequest(new { message = result.Error });
        return BadRequest(new { message = result.Error });
    }

    [HttpPut("{id}")]
    [Authorize]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest request)
    {
        var currentUserId = HttpContext.GetUserId();
        if (!currentUserId.HasValue)
            return Unauthorized(new { message = "User ID required" });

        var result = await _userService.UpdateUserAsync(id, request, currentUserId.Value);
        if (result.Success) return NoContent();
        if (result.NotFound) return NotFound(new { message = result.Error });
        if (result.StatusCode == 403) return Forbid();
        if (result.Conflict) return BadRequest(new { message = result.Error });
        return BadRequest(new { message = result.Error });
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var currentUserId = HttpContext.GetUserId();
        if (!currentUserId.HasValue)
            return Unauthorized(new { message = "User ID required" });

        var result = await _userService.DeleteUserAsync(id, currentUserId.Value);
        if (result.Success) return NoContent();
        if (result.NotFound) return NotFound(new { message = result.Error });
        if (result.StatusCode == 403) return Forbid();
        return BadRequest(new { message = result.Error });
    }

    [HttpPost("{id}/password")]
    [Authorize]
    [Consumes("application/json")]
    public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordRequest request)
    {
        var currentUserId = HttpContext.GetUserId();
        if (!currentUserId.HasValue)
            return Unauthorized(new { message = "User ID required" });

        var result = await _userService.ChangePasswordAsync(id, request, currentUserId.Value);
        if (result.Success) return NoContent();
        if (result.NotFound) return NotFound(new { message = result.Error });
        if (result.StatusCode == 403) return Forbid();
        return BadRequest(new { message = result.Error });
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    [Consumes("application/json")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request)
    {
        // Best-effort: revoke the refresh token so it can never be used again.
        // The access token will expire on its own (60 min); we don't maintain an
        // access-token blocklist to keep things stateless.
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
            await _authService.RevokeRefreshTokenAsync(request.RefreshToken);

        return NoContent();
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [Produces("application/json")]
    public async Task<ActionResult<RefreshTokenResponse>> Refresh([FromBody] RefreshTokenRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
            return BadRequest(new { message = "Refresh token is required" });

        var result = await _authService.RefreshAccessTokenAsync(request.RefreshToken);

        if (result == null)
            return Unauthorized(new { message = "Invalid or expired refresh token" });

        return Ok(new RefreshTokenResponse
        {
            Token = result.Value.AccessToken,
            RefreshToken = result.Value.RefreshToken,
        });
    }

    [HttpGet("health")]
    [AllowAnonymous]
    public async Task<IActionResult> HealthCheck()
    {
        try
        {
            var healthy = await _userService.HealthCheckAsync();
            if (healthy)
                return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
            return StatusCode(503, new { status = "unhealthy" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(503, new { status = "unhealthy", error = ex.Message });
        }
    }
}
