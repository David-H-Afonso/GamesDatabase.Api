using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Services;
using GamesDatabase.Api.Services.Steam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/auth/steam")]
public class SteamAuthController : ControllerBase
{
    private readonly ISteamAuthService _steamAuth;
    private readonly IAuthService _authService;
    private readonly GamesDbContext _context;
    private readonly ISteamApiService _steamApi;
    private readonly SteamSettings _settings;
    private readonly ILogger<SteamAuthController> _logger;

    public SteamAuthController(
        ISteamAuthService steamAuth,
        IAuthService authService,
        GamesDbContext context,
        ISteamApiService steamApi,
        IOptions<SteamSettings> settings,
        ILogger<SteamAuthController> logger)
    {
        _steamAuth = steamAuth;
        _authService = authService;
        _context = context;
        _steamApi = steamApi;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Starts the Steam OpenID login/link flow.
    /// mode=login → no auth required, creates session on callback
    /// mode=link → requires JWT, links Steam to existing account on callback
    /// </summary>
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult StartLogin([FromQuery] string mode = "login")
    {
        if (mode != "login" && mode != "link")
            return BadRequest(new { message = "mode must be 'login' or 'link'" });

        int? userId = null;
        if (mode == "link")
        {
            var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var parsedId))
                return Unauthorized(new { message = "JWT required for link mode" });
            userId = parsedId;
        }

        var nonce = _steamAuth.StoreNonce(userId, mode);
        var callbackUrl = $"{_settings.CallbackBaseUrl}/api/auth/steam/callback";
        var loginUrl = _steamAuth.BuildLoginUrl(nonce, callbackUrl);

        return Redirect(loginUrl);
    }

    /// <summary>
    /// Returns the Steam OpenID URL for the link flow (AJAX-friendly).
    /// Requires JWT authentication so the Bearer token can be sent normally.
    /// </summary>
    [HttpGet("link-url")]
    [Authorize]
    public IActionResult GetLinkUrl()
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return Unauthorized(new { message = "Invalid user" });

        var nonce = _steamAuth.StoreNonce(userId, "link");
        var callbackUrl = $"{_settings.CallbackBaseUrl}/api/auth/steam/callback";
        var loginUrl = _steamAuth.BuildLoginUrl(nonce, callbackUrl);

        return Ok(new { url = loginUrl });
    }

    /// <summary>
    /// Steam OpenID callback. Steam redirects the browser here after login.
    /// Validates the OpenID response and either creates a session or links the account.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback()
    {
        // Parse nonce from return_to URL
        if (!Guid.TryParse(Request.Query["nonce"].ToString(), out var nonce))
        {
            return Redirect(BuildFrontendErrorUrl("invalid_nonce"));
        }

        var nonceData = _steamAuth.ConsumeNonce(nonce);
        if (nonceData == null)
        {
            return Redirect(BuildFrontendErrorUrl("nonce_expired"));
        }

        var steamId = await _steamAuth.ValidateCallbackAsync(Request.Query);
        if (steamId == null)
        {
            return Redirect(BuildFrontendErrorUrl("validation_failed"));
        }

        if (nonceData.Value.Mode == "link")
        {
            return await HandleLinkAsync(nonceData.Value.UserId!.Value, steamId);
        }
        else
        {
            return await HandleLoginAsync(steamId);
        }
    }

    private async Task<IActionResult> HandleLinkAsync(int userId, string steamId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return Redirect(BuildFrontendErrorUrl("user_not_found"));

        // Check if this SteamID is already linked to another account
        var existingLink = await _context.Users.FirstOrDefaultAsync(u => u.SteamId == steamId && u.Id != userId);
        if (existingLink != null)
            return Redirect(BuildFrontendErrorUrl("steam_already_linked"));

        // Fetch Steam profile info
        var profile = await _steamApi.GetPlayerSummaryAsync(steamId);

        user.SteamId = steamId;
        user.SteamNickname = profile?.Nickname;
        user.SteamAvatarUrl = profile?.AvatarUrl;
        user.SteamLinkedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var redirectUrl = $"{_settings.FrontendBaseUrl}/#/steam-callback?steamLinked=true" +
            $"&steamId={Uri.EscapeDataString(steamId)}" +
            (profile?.Nickname != null ? $"&steamNickname={Uri.EscapeDataString(profile.Nickname)}" : "") +
            (profile?.AvatarUrl != null ? $"&steamAvatarUrl={Uri.EscapeDataString(profile.AvatarUrl)}" : "");

        return Redirect(redirectUrl);
    }

    private async Task<IActionResult> HandleLoginAsync(string steamId)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.SteamId == steamId);
        if (user == null)
            return Redirect(BuildFrontendErrorUrl("no_account_linked"));

        var token = _authService.GenerateToken(user);

        var redirectUrl = $"{_settings.FrontendBaseUrl}/#/steam-callback" +
            $"?token={Uri.EscapeDataString(token)}" +
            $"&userId={user.Id}" +
            $"&username={Uri.EscapeDataString(user.Username)}" +
            $"&role={Uri.EscapeDataString(user.Role.ToString())}";

        return Redirect(redirectUrl);
    }

    private string BuildFrontendErrorUrl(string error)
        => $"{_settings.FrontendBaseUrl}/#/steam-callback?error={Uri.EscapeDataString(error)}";
}
