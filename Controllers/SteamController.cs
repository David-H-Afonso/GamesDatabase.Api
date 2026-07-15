using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts.Steam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/steam")]
[Authorize]
public class SteamController : BaseApiController
{
    private readonly ISteamProfileService _profile;
    private readonly ISteamSyncService _steamSync;
    private readonly ISteamStoreService _steamStore;

    public SteamController(
        ISteamProfileService profile,
        ISteamSyncService steamSync,
        ISteamStoreService steamStore)
    {
        _profile = profile;
        _steamSync = steamSync;
        _steamStore = steamStore;
    }

    /// <summary>Returns the Steam profile data linked to the current user.</summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _profile.GetProfileAsync(userId.Value);
        if (result == null) return NotFound(new { message = "No Steam account linked" });
        return Ok(result);
    }

    /// <summary>Unlinks the Steam account from the current user.</summary>
    [HttpDelete("link")]
    public async Task<IActionResult> UnlinkSteam()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        await _profile.UnlinkSteamAsync(userId.Value);
        return NoContent();
    }

    /// <summary>Links a Steam account manually by SteamID64. Useful for desktop builds where OpenID redirects are not available.</summary>
    [HttpPost("link/manual")]
    public async Task<IActionResult> LinkSteamManually([FromBody] SteamManualLinkRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _profile.LinkSteamManuallyAsync(userId.Value, request.SteamId);
        if (result == null)
            return BadRequest(new { message = "SteamID must be a 17-digit SteamID64 or a Steam profile URL containing it." });
        return Ok(result);
    }

    /// <summary>Links an existing GDB game to a Steam AppID.</summary>
    [HttpPost("link-game")]
    public async Task<IActionResult> LinkGame([FromBody] SteamLinkGameRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var (success, error, result) = await _profile.LinkGameAsync(userId.Value, request);
        if (!success) return NotFound(new { message = error });
        return Ok(result);
    }

    /// <summary>Returns the user's Steam library with GDB match status.</summary>
    [HttpGet("library")]
    public async Task<IActionResult> GetLibrary()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _profile.GetLibraryAsync(userId.Value);
        if (result == null) return BadRequest(new { message = "No Steam account linked" });
        return Ok(result);
    }

    /// <summary>Imports selected Steam games into GDB.</summary>
    [HttpPost("import")]
    public async Task<IActionResult> ImportGames([FromBody] SteamImportRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        if ((request.AppIds == null || request.AppIds.Count == 0) && (request.Games == null || request.Games.Count == 0))
            return BadRequest(new { message = "No AppIDs provided" });

        var result = await _steamSync.ImportLibraryAsync(userId.Value, request);
        return Ok(result);
    }

    /// <summary>Syncs all GDB games that have a SteamAppId for the current user.</summary>
    [HttpPost("sync")]
    public async Task<IActionResult> SyncAll()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _steamSync.SyncAllUserGamesAsync(userId.Value);
        if (!result.Success) return BadRequest(new { message = result.Error });
        return Ok(result);
    }

    /// <summary>Syncs a specific GDB game with its Steam data.</summary>
    [HttpPost("sync/{gameId:int}")]
    public async Task<IActionResult> SyncGame(int gameId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _steamSync.SyncGameAsync(userId.Value, gameId);
        if (!result.Success) return BadRequest(new { message = result.Error });
        return Ok(result);
    }

    /// <summary>Returns stored achievements for a GDB game.</summary>
    [HttpGet("achievements/{gameId:int}")]
    public async Task<IActionResult> GetAchievements(int gameId)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _profile.GetAchievementsAsync(userId.Value, gameId);
        return Ok(result);
    }

    /// <summary>Returns cached store metadata for a Steam AppID. Fetches from Steam Store if not cached or stale.</summary>
    [HttpGet("app/{appId:int}/metadata")]
    public async Task<IActionResult> GetAppMetadata(int appId)
    {
        var details = await _steamStore.GetOrCacheAppDetailsAsync(appId);
        if (details == null)
            return NotFound(new { message = $"Could not retrieve metadata for AppID {appId}" });
        return Ok(details);
    }

    /// <summary>Suggests GDB games that might match unlinked Steam library games by name similarity.</summary>
    [HttpGet("match-suggestions")]
    public async Task<IActionResult> GetMatchSuggestions()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _profile.GetMatchSuggestionsAsync(userId.Value);
        return Ok(result);
    }

    /// <summary>Suggests Steam Store apps that might match GDB games without a Steam AppID.</summary>
    [HttpGet("store-match-suggestions")]
    public async Task<IActionResult> GetStoreMatchSuggestions()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _profile.GetStoreMatchSuggestionsAsync(userId.Value);
        return Ok(result);
    }

    /// <summary>Stores rejected Steam/GDB suggestion pairs so future searches can offer other matches.</summary>
    [HttpPost("match-suggestions/dismiss")]
    public async Task<IActionResult> DismissMatchSuggestions([FromBody] SteamDismissMatchSuggestionsRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var (dismissed, error) = await _profile.DismissMatchSuggestionsAsync(userId.Value, request);
        if (error != null) return BadRequest(new { message = error });
        return Ok(new { dismissed });
    }

    /// <summary>Suggests start/finish dates from Steam activity for linked GDB games missing dates or with manual conflicts.</summary>
    [HttpGet("date-suggestions")]
    public async Task<IActionResult> GetDateSuggestions([FromQuery] int? gameId = null, [FromQuery] bool includeStarted = true)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _profile.GetDateSuggestionsAsync(userId.Value, gameId, includeStarted);
        if (result == null) return BadRequest(new { message = "No Steam account linked" });
        return Ok(result);
    }

    [HttpPost("date-suggestions/apply")]
    public async Task<IActionResult> ApplyDateSuggestions([FromBody] SteamApplyDateSuggestionsRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _profile.ApplyDateSuggestionsAsync(userId.Value, request);
        if (result == null) return BadRequest(new { message = "No date suggestions provided" });
        return Ok(result);
    }

    [HttpPost("date-suggestions/dismiss")]
    public async Task<IActionResult> DismissDateSuggestions([FromBody] SteamDismissDateSuggestionsRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var (dismissed, error) = await _profile.DismissDateSuggestionsAsync(userId.Value, request);
        if (error != null) return BadRequest(new { message = error });
        return Ok(new { dismissed });
    }

    /// <summary>Searches the Steam Store for any game by name (does not require ownership).</summary>
    [HttpGet("store/search")]
    public async Task<IActionResult> SearchStore([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return BadRequest(new { message = "La búsqueda debe tener al menos 2 caracteres" });

        var results = await _steamStore.SearchStoreAsync(q);
        return Ok(results);
    }

    /// <summary>Adds a Steam Store game (not necessarily owned) to GDB.</summary>
    [HttpPost("store/add")]
    public async Task<IActionResult> AddStoreGame([FromBody] SteamAddStoreGameRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var result = await _steamSync.AddStoreGameAsync(userId.Value, request.AppId, request.LogoUrl, request.HeroUrl, request.CoverUrl);
        return Ok(result);
    }
}
