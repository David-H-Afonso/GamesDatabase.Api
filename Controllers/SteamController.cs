using GamesDatabase.Api.Data;
using GamesDatabase.Api.DTOs.Steam;
using GamesDatabase.Api.Services.Steam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/steam")]
[Authorize]
public class SteamController : BaseApiController
{
    private readonly GamesDbContext _context;
    private readonly ISteamApiService _steamApi;
    private readonly ISteamStoreService _steamStore;
    private readonly ISteamSyncService _steamSync;
    private readonly ILogger<SteamController> _logger;

    public SteamController(
        GamesDbContext context,
        ISteamApiService steamApi,
        ISteamStoreService steamStore,
        ISteamSyncService steamSync,
        ILogger<SteamController> logger)
    {
        _context = context;
        _steamApi = steamApi;
        _steamStore = steamStore;
        _steamSync = steamSync;
        _logger = logger;
    }

    /// <summary>Returns the Steam profile data linked to the current user.</summary>
    [HttpGet("profile")]
    public async Task<IActionResult> GetProfile()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var user = await _context.Users.FindAsync(userId.Value);
        if (user?.SteamId == null)
            return NotFound(new { message = "No Steam account linked" });

        return Ok(new SteamProfileResponse
        {
            SteamId = user.SteamId,
            SteamNickname = user.SteamNickname ?? user.SteamId,
            SteamAvatarUrl = user.SteamAvatarUrl,
            SteamLinkedAt = user.SteamLinkedAt ?? DateTime.UtcNow
        });
    }

    /// <summary>Unlinks the Steam account from the current user.</summary>
    [HttpDelete("link")]
    public async Task<IActionResult> UnlinkSteam()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return NotFound();

        user.SteamId = null;
        user.SteamNickname = null;
        user.SteamAvatarUrl = null;
        user.SteamLinkedAt = null;
        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>Links an existing GDB game to a Steam AppID.</summary>
    [HttpPost("link-game")]
    public async Task<IActionResult> LinkGame([FromBody] SteamLinkGameRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var game = await _context.Games.FirstOrDefaultAsync(g => g.Id == request.GameId && g.UserId == userId.Value);
        if (game == null) return NotFound(new { message = "Game not found" });

        game.SteamAppId = request.AppId;

        // Sync playtime if user has Steam linked
        var user = await _context.Users.FindAsync(userId.Value);
        if (user?.SteamId != null)
        {
            var ownedGames = await _steamApi.GetOwnedGamesAsync(user.SteamId);
            var owned = ownedGames.FirstOrDefault(g => g.AppId == request.AppId);
            if (owned != null)
            {
                game.SteamPlaytimeForever = owned.PlaytimeForever;
                game.SteamPlaytime2Weeks = owned.Playtime2Weeks;
                game.SteamLastSynced = DateTime.UtcNow;
            }
        }

        await _context.SaveChangesAsync();
        return Ok(new { gameId = game.Id, appId = request.AppId });
    }

    /// <summary>Returns the user's Steam library with GDB match status.</summary>
    [HttpGet("library")]
    public async Task<IActionResult> GetLibrary()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var user = await _context.Users.FindAsync(userId.Value);
        if (user?.SteamId == null)
            return BadRequest(new { message = "No Steam account linked" });

        var ownedGames = await _steamApi.GetOwnedGamesAsync(user.SteamId);

        // Load GDB games with SteamAppId for this user
        var gdbGames = await _context.Games
            .Where(g => g.UserId == userId.Value && g.SteamAppId != null)
            .Select(g => new { g.SteamAppId, g.Id, g.Name })
            .ToListAsync();

        var gdbByAppId = gdbGames.ToDictionary(g => g.SteamAppId!.Value, g => g);

        var result = ownedGames.Select(g =>
        {
            gdbByAppId.TryGetValue(g.AppId, out var gdb);
            return new SteamLibraryGameDto
            {
                AppId = g.AppId,
                Name = g.Name,
                PlaytimeForever = g.PlaytimeForever,
                Playtime2Weeks = g.Playtime2Weeks,
                IconUrl = g.IconUrl,
                GdbGameId = gdb?.Id,
                GdbGameName = gdb?.Name
            };
        }).ToList();

        return Ok(result);
    }

    /// <summary>Imports selected Steam games into GDB.</summary>
    [HttpPost("import")]
    public async Task<IActionResult> ImportGames([FromBody] SteamImportRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        if (request.AppIds == null || request.AppIds.Count == 0)
            return BadRequest(new { message = "No AppIDs provided" });

        var result = await _steamSync.ImportLibraryAsync(userId.Value, request.AppIds, request.CreateMissing);
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

        var game = await _context.Games.FindAsync(gameId);
        if (game == null || game.UserId != userId.Value) return NotFound();

        var achievements = await _context.SteamAchievements
            .Where(a => a.UserId == userId.Value && a.GameId == gameId)
            .OrderByDescending(a => a.Achieved)
            .ThenByDescending(a => a.UnlockTime)
            .Select(a => new SteamAchievementDto
            {
                Id = a.Id,
                SteamAppId = a.SteamAppId,
                ApiName = a.ApiName,
                DisplayName = a.DisplayName,
                Description = a.Description,
                Achieved = a.Achieved,
                UnlockTime = a.UnlockTime,
                IconUrl = a.IconUrl,
                IconGrayUrl = a.IconGrayUrl,
                Hidden = a.Hidden
            })
            .ToListAsync();

        return Ok(achievements);
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

        var user = await _context.Users.FindAsync(userId.Value);
        if (user?.SteamId == null)
            return BadRequest(new { message = "No Steam account linked" });

        var steamGames = await _steamApi.GetOwnedGamesAsync(user.SteamId);

        var gdbGames = await _context.Games
            .Where(g => g.UserId == userId.Value)
            .Select(g => new { g.Id, g.Name, g.SteamAppId })
            .ToListAsync();

        var linkedAppIds = gdbGames
            .Where(g => g.SteamAppId.HasValue)
            .Select(g => g.SteamAppId!.Value)
            .ToHashSet();

        var unlinkedSteam = steamGames.Where(g => !linkedAppIds.Contains(g.AppId)).ToList();
        var unlinkedGdb = gdbGames.Where(g => !g.SteamAppId.HasValue).ToList();

        var suggestions = new List<SteamMatchSuggestionDto>();

        foreach (var sg in unlinkedSteam)
        {
            var steamNorm = NormalizeName(sg.Name);
            SteamMatchSuggestionDto? best = null;

            foreach (var gg in unlinkedGdb)
            {
                var gdbNorm = NormalizeName(gg.Name);
                var score = NameScore(steamNorm, gdbNorm);
                if (score >= 50 && (best == null || score > best.Confidence))
                {
                    best = new SteamMatchSuggestionDto
                    {
                        SteamAppId = sg.AppId,
                        SteamName = sg.Name,
                        SteamIconUrl = sg.IconUrl,
                        GdbGameId = gg.Id,
                        GdbGameName = gg.Name,
                        Confidence = score
                    };
                }
            }

            if (best != null)
                suggestions.Add(best);
        }

        return Ok(suggestions.OrderByDescending(s => s.Confidence).ToList());
    }

    private static string NormalizeName(string name) =>
        new string(name.ToLowerInvariant()
            .Where(c => char.IsLetterOrDigit(c) || c == ' ')
            .ToArray()).Trim();

    private static int NameScore(string a, string b)
    {
        if (a == b) return 100;

        var wa = new HashSet<string>(a.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var wb = new HashSet<string>(b.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        if (wa.Count == 0 || wb.Count == 0) return 0;

        int intersection = wa.Intersect(wb).Count();
        if (intersection == 0) return 0;

        // All words of the shorter name appear in the longer → strong match
        if (wa.IsSubsetOf(wb) || wb.IsSubsetOf(wa))
            return Math.Max(80, (int)(100.0 * intersection / Math.Max(wa.Count, wb.Count)));

        // Jaccard similarity
        int union = wa.Union(wb).Count();
        return (int)(100.0 * intersection / union);
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

        var result = await _steamSync.AddStoreGameAsync(userId.Value, request.AppId);
        return Ok(result);
    }
}
