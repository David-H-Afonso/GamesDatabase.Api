using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Application.Services.Steam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
    private readonly CorsSettings _corsSettings;
    private readonly ILogger<SteamAuthController> _logger;

    public SteamAuthController(
        ISteamAuthService steamAuth,
        IAuthService authService,
        GamesDbContext context,
        ISteamApiService steamApi,
        IOptions<SteamSettings> settings,
        IOptions<CorsSettings> corsSettings,
        ILogger<SteamAuthController> logger)
    {
        _steamAuth = steamAuth;
        _authService = authService;
        _context = context;
        _steamApi = steamApi;
        _settings = settings.Value;
        _corsSettings = corsSettings.Value;
        _logger = logger;
    }

    /// <summary>
    /// Starts the Steam OpenID login/link flow.
    /// mode=login → no auth required, creates session on callback
    /// mode=link  → requires JWT, links Steam to existing account on callback
    /// </summary>
    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult StartLogin([FromQuery] string mode = "login", [FromQuery] string? frontend_url = null)
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

        var frontendBase = string.IsNullOrWhiteSpace(frontend_url) ? GetFrontendBaseUrl() : ResolveRequestedFrontendUrl(frontend_url);
        if (frontendBase is null)
            return BadRequest(new { message = "A configured or allowlisted frontend URL is required." });
        var callbackUrl = $"{frontendBase}/api/auth/steam/callback" +
            $"?frontend_url={Uri.EscapeDataString(frontendBase)}";
        var nonce = _steamAuth.StoreNonce(userId, mode, frontendBase, callbackUrl);
        var loginUrl = _steamAuth.BuildLoginUrl(nonce);

        return Redirect(loginUrl);
    }

    /// <summary>
    /// Returns the Steam OpenID URL for the link flow (AJAX-friendly).
    /// </summary>
    [HttpGet("link-url")]
    [Authorize]
    public IActionResult GetLinkUrl([FromQuery] string? frontend_url = null)
    {
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            return Unauthorized(new { message = "Invalid user" });

        var frontendBase = string.IsNullOrWhiteSpace(frontend_url) ? GetFrontendBaseUrl() : ResolveRequestedFrontendUrl(frontend_url);
        if (frontendBase is null)
            return BadRequest(new { message = "A configured or allowlisted frontend URL is required." });
        var callbackUrl = $"{frontendBase}/api/auth/steam/callback" +
            $"?frontend_url={Uri.EscapeDataString(frontendBase)}";
        var nonce = _steamAuth.StoreNonce(userId, "link", frontendBase, callbackUrl);
        var loginUrl = _steamAuth.BuildLoginUrl(nonce);

        return Ok(new { url = loginUrl });
    }

    /// <summary>
    /// Steam OpenID callback. Validates the OpenID response and either links
    /// the account or stores a short-lived one-time code for token exchange.
    /// The JWT is never put in the redirect URL — the frontend uses /exchange.
    /// </summary>
    [HttpGet("callback")]
    [AllowAnonymous]
    public async Task<IActionResult> Callback()
    {
        if (Request.Query["nonce"].Count != 1 || !Guid.TryParse(Request.Query["nonce"][0], out var nonce))
            return BadRequest(new { message = "Invalid Steam login state. Start the login again." });

        var nonceData = _steamAuth.ConsumeNonce(nonce);
        if (nonceData == null)
            return BadRequest(new { message = "Steam login state is unavailable or expired. Start the login again." });

        var frontendBase = nonceData.FrontendBaseUrl;
        if (Request.Query["frontend_url"].Count != 1 || Request.Query["frontend_url"][0] != frontendBase)
            return Redirect(BuildFrontendErrorUrl(frontendBase, "validation_failed"));

        var steamId = await _steamAuth.ValidateCallbackAsync(Request.Query, nonceData.ReturnTo, HttpContext.RequestAborted);
        if (steamId == null)
            return Redirect(BuildFrontendErrorUrl(frontendBase, "validation_failed"));

        if (nonceData.Mode == "link")
            return await HandleLinkAsync(nonceData.UserId!.Value, steamId, frontendBase);
        else
            return await HandleLoginAsync(steamId, frontendBase);
    }

    /// <summary>
    /// Exchanges a short-lived one-time code (issued by the Steam callback) for
    /// the actual JWT access token + refresh token. The code expires in 5 minutes
    /// and can only be used once, so the JWT is never transmitted through the URL.
    /// </summary>
    [HttpGet("exchange/{code}")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public IActionResult Exchange(Guid code)
    {
        var result = _steamAuth.ConsumeLoginResult(code);
        if (result == null)
            return BadRequest(new { message = "Invalid or expired Steam login code" });

        return Ok(new
        {
            token = result.Token,
            refreshToken = result.RefreshToken,
            userId = result.UserId,
            username = result.Username,
            role = result.Role,
            steamId = result.SteamId,
            steamNickname = result.SteamNickname,
            steamAvatarUrl = result.SteamAvatarUrl,
        });
    }

    // ── Private handlers ──────────────────────────────────────────────────────

    private async Task<IActionResult> HandleLinkAsync(int userId, string steamId, string frontendBase)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
            return Redirect(BuildFrontendErrorUrl(frontendBase, "user_not_found"));

        var existingLink = await _context.Users
            .FirstOrDefaultAsync(u => u.SteamId == steamId && u.Id != userId);
        if (existingLink != null)
            return Redirect(BuildFrontendErrorUrl(frontendBase, "steam_already_linked"));

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

        var accessToken = _authService.GenerateToken(user);
        var refreshToken = await _authService.GenerateAndStoreRefreshTokenAsync(user.Id);

        // Store tokens behind a one-time code — never put the JWT in the URL
        var loginResult = new SteamLoginResult(
            Token: accessToken,
            RefreshToken: refreshToken,
            UserId: user.Id,
            Username: user.Username,
            Role: user.Role.ToString(),
            SteamId: user.SteamId,
            SteamNickname: user.SteamNickname,
            SteamAvatarUrl: user.SteamAvatarUrl);

        var code = _steamAuth.StoreLoginResult(loginResult);

        // Redirect with an opaque 5-minute code; the frontend exchanges it immediately
        return Redirect($"{frontendBase}/#/steam-callback?code={code}");
    }

    // ── URL helpers ───────────────────────────────────────────────────────────

    private string? GetFrontendBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_settings.FrontendBaseUrl))
            return Uri.TryCreate(_settings.FrontendBaseUrl, UriKind.Absolute, out var configured)
                && configured.Scheme is "http" or "https" && string.IsNullOrEmpty(configured.UserInfo)
                && string.IsNullOrEmpty(configured.Query) && string.IsNullOrEmpty(configured.Fragment)
                ? configured.GetLeftPart(UriPartial.Path).TrimEnd('/') : null;

        // A Referer is caller-controlled too; it may select only an existing allowed origin.
        return ResolveRequestedFrontendUrl(Request.Headers.Referer.ToString());
    }

    private static string BuildFrontendErrorUrl(string frontendBase, string error)
        => $"{frontendBase}/#/steam-callback?error={Uri.EscapeDataString(error)}";

    private string? ResolveRequestedFrontendUrl(string? requestedUrl)
    {
        if (string.IsNullOrWhiteSpace(requestedUrl))
            return null;

        if (!Uri.TryCreate(requestedUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
            return null;

        var origin = $"{uri.Scheme}://{uri.Authority}";

        if (_corsSettings.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            return origin;

        if (!string.IsNullOrWhiteSpace(_settings.FrontendBaseUrl) &&
            string.Equals(origin, _settings.FrontendBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            return origin;

        _logger.LogWarning("Rejected untrusted Steam frontend URL.");
        return null;
    }
}
