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
        var frontendBase = GetFrontendBaseUrl();
        var callbackUrl = $"{GetCallbackBaseUrl()}/api/auth/steam/callback" +
            $"?frontend_url={Uri.EscapeDataString(frontendBase)}";
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
        var frontendBase = GetFrontendBaseUrl();
        var callbackUrl = $"{GetCallbackBaseUrl()}/api/auth/steam/callback" +
            $"?frontend_url={Uri.EscapeDataString(frontendBase)}";
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
        // Recover frontend URL embedded in the return_to URL
        var frontendBase = Request.Query["frontend_url"].ToString();
        if (string.IsNullOrWhiteSpace(frontendBase))
            frontendBase = GetFrontendBaseUrl();

        // Parse nonce appended by BuildLoginUrl to the return_to URL
        if (!Guid.TryParse(Request.Query["nonce"].ToString(), out var nonce))
            return Redirect(BuildFrontendErrorUrl(frontendBase, "invalid_nonce"));

        var nonceData = _steamAuth.ConsumeNonce(nonce);
        if (nonceData == null)
            return Redirect(BuildFrontendErrorUrl(frontendBase, "nonce_expired"));

        var steamId = await _steamAuth.ValidateCallbackAsync(Request.Query);
        if (steamId == null)
            return Redirect(BuildFrontendErrorUrl(frontendBase, "validation_failed"));

        if (nonceData.Value.Mode == "link")
            return await HandleLinkAsync(nonceData.Value.UserId!.Value, steamId, frontendBase);
        else
            return await HandleLoginAsync(steamId, frontendBase);
    }

    private async Task<IActionResult> HandleLinkAsync(int userId, string steamId, string frontendBase)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return Redirect(BuildFrontendErrorUrl(frontendBase, "user_not_found"));

        // Check if this SteamID is already linked to another account
        var existingLink = await _context.Users.FirstOrDefaultAsync(u => u.SteamId == steamId && u.Id != userId);
        if (existingLink != null)
            return Redirect(BuildFrontendErrorUrl(frontendBase, "steam_already_linked"));

        // Fetch Steam profile info
        var profile = await _steamApi.GetPlayerSummaryAsync(steamId);

        user.SteamId = steamId;
        user.SteamNickname = profile?.Nickname;
        user.SteamAvatarUrl = profile?.AvatarUrl;
        user.SteamLinkedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        var redirectUrl = $"{frontendBase}/#/steam-callback?steamLinked=true" +
            $"&steamId={Uri.EscapeDataString(steamId)}" +
            (profile?.Nickname != null ? $"&steamNickname={Uri.EscapeDataString(profile.Nickname)}" : "") +
            (profile?.AvatarUrl != null ? $"&steamAvatarUrl={Uri.EscapeDataString(profile.AvatarUrl)}" : "");

        return Redirect(redirectUrl);
    }

    private async Task<IActionResult> HandleLoginAsync(string steamId, string frontendBase)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.SteamId == steamId);
        if (user == null)
            return Redirect(BuildFrontendErrorUrl(frontendBase, "no_account_linked"));

        var token = _authService.GenerateToken(user);

        var redirectUrl = $"{frontendBase}/#/steam-callback" +
            $"?token={Uri.EscapeDataString(token)}" +
            $"&userId={user.Id}" +
            $"&username={Uri.EscapeDataString(user.Username)}" +
            $"&role={Uri.EscapeDataString(user.Role.ToString())}";

        return Redirect(redirectUrl);
    }

    /// <summary>
    /// Resolves the API's own base URL for building the Steam callback URL.
    /// Uses explicit config when set; otherwise auto-detects from the incoming request
    /// (supports X-Forwarded-Proto / X-Forwarded-Host for reverse proxies).
    /// </summary>
    private string GetCallbackBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_settings.CallbackBaseUrl))
            return _settings.CallbackBaseUrl.TrimEnd('/');

        var scheme = Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) && !string.IsNullOrEmpty(proto)
            ? proto.ToString().Split(',')[0].Trim()
            : Request.Scheme;

        var host = Request.Headers.TryGetValue("X-Forwarded-Host", out var fwdHost) && !string.IsNullOrEmpty(fwdHost)
            ? fwdHost.ToString().Split(',')[0].Trim()
            : Request.Host.ToString();

        return $"{scheme}://{host}";
    }

    /// <summary>
    /// Resolves the frontend base URL for post-login redirects.
    /// Uses explicit config when set; otherwise reads the Referer header
    /// (browsers send this when window.location.href navigates to the API).
    /// </summary>
    private string GetFrontendBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_settings.FrontendBaseUrl))
            return _settings.FrontendBaseUrl.TrimEnd('/');

        var referer = Request.Headers.Referer.ToString();
        if (!string.IsNullOrWhiteSpace(referer) && Uri.TryCreate(referer, UriKind.Absolute, out var refUri))
            return $"{refUri.Scheme}://{refUri.Authority}";

        _logger.LogWarning("Cannot determine frontend base URL: no FrontendBaseUrl config and no Referer header.");
        return string.Empty;
    }

    private static string BuildFrontendErrorUrl(string frontendBase, string error)
        => $"{frontendBase}/#/steam-callback?error={Uri.EscapeDataString(error)}";
}
