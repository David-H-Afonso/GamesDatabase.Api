using GamesDatabase.Api.Data;
using GamesDatabase.Api.DTOs.Steam;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.Services.Steam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
    private readonly IMemoryCache _cache;

    public SteamController(
        GamesDbContext context,
        ISteamApiService steamApi,
        ISteamStoreService steamStore,
        ISteamSyncService steamSync,
        ILogger<SteamController> logger,
        IMemoryCache cache)
    {
        _context = context;
        _steamApi = steamApi;
        _steamStore = steamStore;
        _steamSync = steamSync;
        _logger = logger;
        _cache = cache;
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

    /// <summary>Links a Steam account manually by SteamID64. Useful for desktop builds where OpenID redirects are not available.</summary>
    [HttpPost("link/manual")]
    public async Task<IActionResult> LinkSteamManually([FromBody] SteamManualLinkRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var steamId = ExtractSteamId(request.SteamId);
        if (steamId == null)
            return BadRequest(new { message = "SteamID must be a 17-digit SteamID64 or a Steam profile URL containing it." });

        var user = await _context.Users.FindAsync(userId.Value);
        if (user == null) return NotFound(new { message = "User not found" });

        var existingLink = await _context.Users.FirstOrDefaultAsync(u => u.SteamId == steamId && u.Id != userId.Value);
        if (existingLink != null)
            return BadRequest(new { message = "This Steam account is already linked to another user" });

        var profile = await _steamApi.GetPlayerSummaryAsync(steamId);

        user.SteamId = steamId;
        user.SteamNickname = !string.IsNullOrWhiteSpace(profile?.Nickname) ? profile.Nickname : steamId;
        user.SteamAvatarUrl = profile?.AvatarUrl;
        user.SteamLinkedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new SteamProfileResponse
        {
            SteamId = steamId,
            SteamNickname = user.SteamNickname ?? steamId,
            SteamAvatarUrl = user.SteamAvatarUrl,
            ProfileUrl = profile?.ProfileUrl,
            IsPublic = profile?.IsPublic ?? false,
            SteamLinkedAt = user.SteamLinkedAt ?? DateTime.UtcNow
        });
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
            var ownedGames = await GetCachedOwnedGamesAsync(user.SteamId);
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

        var ownedGames = await GetCachedOwnedGamesAsync(user.SteamId);

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

        var steamGames = await GetCachedOwnedGamesAsync(user.SteamId);

        var gdbGames = await _context.Games
            .Where(g => g.UserId == userId.Value)
            .Select(g => new MatchGameCandidate(g.Id, g.Name, g.SteamAppId))
            .ToListAsync();

        var linkedAppIds = gdbGames
            .Where(g => g.SteamAppId.HasValue)
            .Select(g => g.SteamAppId!.Value)
            .ToHashSet();

        var unlinkedSteam = steamGames.Where(g => !linkedAppIds.Contains(g.AppId)).ToList();
        var unlinkedGdb = gdbGames.Where(g => !g.SteamAppId.HasValue).ToList();
        var dismissedPairs = (await _context.SteamMatchDismissals
            .Where(d => d.UserId == userId.Value)
            .Select(d => new { d.SteamAppId, d.GameId })
            .ToListAsync())
            .Select(d => MatchKey(d.SteamAppId, d.GameId))
            .ToHashSet();

        var suggestions = unlinkedSteam
            .Select(sg => FindBestMatchSuggestion(
                sg.AppId,
                sg.Name,
                sg.IconUrl,
                unlinkedGdb,
                dismissedPairs))
            .Where(s => s != null)
            .Cast<SteamMatchSuggestionDto>()
            .ToList();

        return Ok(suggestions.OrderByDescending(s => s.Confidence).ToList());
    }

    /// <summary>Suggests Steam Store apps that might match GDB games without a Steam AppID.</summary>
    [HttpGet("store-match-suggestions")]
    public async Task<IActionResult> GetStoreMatchSuggestions()
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var gdbGames = await _context.Games
            .Where(g => g.UserId == userId.Value && !g.SteamAppId.HasValue)
            .OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE"))
            .Select(g => new MatchGameCandidate(g.Id, g.Name, null))
            .ToListAsync();

        var dismissedPairs = (await _context.SteamMatchDismissals
            .Where(d => d.UserId == userId.Value)
            .Select(d => new { d.SteamAppId, d.GameId })
            .ToListAsync())
            .Select(d => MatchKey(d.SteamAppId, d.GameId))
            .ToHashSet();

        var suggestions = new List<SteamMatchSuggestionDto>();

        foreach (var gg in gdbGames)
        {
            List<SteamStoreSearchItemDto> storeResults;
            try
            {
                storeResults = await GetCachedStoreSearchAsync(gg.Name);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Skipping Steam Store suggestion search for game {GameId} '{GameName}'", gg.Id, gg.Name);
                continue;
            }

            var best = storeResults
                .Take(10)
                .Select(storeGame => FindMatchSuggestion(
                    storeGame.AppId,
                    storeGame.Name,
                    storeGame.LogoUrl ?? storeGame.CoverUrl,
                    gg.Id,
                    gg.Name,
                    dismissedPairs))
                .Where(s => s != null)
                .Cast<SteamMatchSuggestionDto>()
                .OrderByDescending(s => s.Confidence)
                .FirstOrDefault();

            if (best != null)
                suggestions.Add(best);
        }

        return Ok(suggestions.OrderByDescending(s => s.Confidence).ThenBy(s => s.GdbGameName).ToList());
    }

    /// <summary>Stores rejected Steam/GDB suggestion pairs so future searches can offer other matches.</summary>
    [HttpPost("match-suggestions/dismiss")]
    public async Task<IActionResult> DismissMatchSuggestions([FromBody] SteamDismissMatchSuggestionsRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var requestedPairs = request.Suggestions
            .Where(s => s.SteamAppId > 0 && s.GdbGameId > 0)
            .GroupBy(s => new { s.SteamAppId, s.GdbGameId })
            .Select(g => g.Key)
            .ToList();

        if (requestedPairs.Count == 0)
            return BadRequest(new { message = "No suggestions provided" });

        var gameIds = requestedPairs.Select(p => p.GdbGameId).Distinct().ToList();
        var validGameIds = (await _context.Games
            .Where(g => g.UserId == userId.Value && gameIds.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync())
            .ToHashSet();

        var steamAppIds = requestedPairs.Select(p => p.SteamAppId).Distinct().ToList();
        var existingPairs = (await _context.SteamMatchDismissals
            .Where(d => d.UserId == userId.Value && steamAppIds.Contains(d.SteamAppId))
            .Select(d => new { d.SteamAppId, d.GameId })
            .ToListAsync())
            .Select(d => MatchKey(d.SteamAppId, d.GameId))
            .ToHashSet();

        var created = 0;
        foreach (var pair in requestedPairs)
        {
            if (!validGameIds.Contains(pair.GdbGameId))
                continue;

            var key = MatchKey(pair.SteamAppId, pair.GdbGameId);
            if (existingPairs.Contains(key))
                continue;

            _context.SteamMatchDismissals.Add(new SteamMatchDismissal
            {
                UserId = userId.Value,
                SteamAppId = pair.SteamAppId,
                GameId = pair.GdbGameId,
                CreatedAt = DateTime.UtcNow
            });
            existingPairs.Add(key);
            created++;
        }

        if (created > 0)
            await _context.SaveChangesAsync();

        return Ok(new { dismissed = created });
    }

    /// <summary>Suggests start/finish dates from Steam activity for linked GDB games missing dates or with manual conflicts.</summary>
    [HttpGet("date-suggestions")]
    public async Task<IActionResult> GetDateSuggestions([FromQuery] int? gameId = null, [FromQuery] bool includeStarted = true)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var user = await _context.Users.FindAsync(userId.Value);
        if (user?.SteamId == null)
            return BadRequest(new { message = "No Steam account linked" });

        var gamesQuery = _context.Games
            .AsNoTracking()
            .Where(g => g.UserId == userId.Value && g.SteamAppId.HasValue);

        if (gameId.HasValue)
            gamesQuery = gamesQuery.Where(g => g.Id == gameId.Value);

        var games = await gamesQuery
            .OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE"))
            .ToListAsync();

        var ownedGames = (await GetCachedOwnedGamesAsync(user.SteamId))
            .ToDictionary(g => g.AppId, g => g);

        var suggestions = new List<SteamDateSuggestionDto>();

        foreach (var game in games)
        {
            var appId = game.SteamAppId!.Value;
            ownedGames.TryGetValue(appId, out var ownedGame);
            var appDetails = ownedGame == null ? await _steamStore.GetOrCacheAppDetailsAsync(appId) : null;

            var notes = new List<string>();
            var proposedFinished = ownedGame?.LastPlayedAt?.ToString("yyyy-MM-dd");
            var finishedSource = proposedFinished != null ? "lastPlayed" : "none";
            if (proposedFinished == null)
                notes.Add("noLastPlayed");

            var hasFinished = !string.IsNullOrWhiteSpace(game.Finished);
            var isSteamManagedFinished = game.SteamFinishedSource == "steam";
            var isRejectedFinished = proposedFinished != null && game.SteamFinishedRejectedValue == proposedFinished;
            var shouldSuggestFinished = proposedFinished != null
                && !isRejectedFinished
                && (!hasFinished || (!isSteamManagedFinished && game.Finished != proposedFinished));
            var isFinishedConflict = shouldSuggestFinished && hasFinished && !isSteamManagedFinished && game.Finished != proposedFinished;

            string? proposedStarted = null;
            var startedSource = "none";
            if (includeStarted && string.IsNullOrWhiteSpace(game.Started))
            {
                var firstUnlock = await GetFirstAchievementUnlockAsync(userId.Value, user.SteamId, appId);
                if (firstUnlock.HasValue)
                {
                    proposedStarted = firstUnlock.Value.ToString("yyyy-MM-dd");
                    startedSource = "firstAchievement";
                }
                else
                {
                    notes.Add("noFirstAchievement");
                }
            }
            else if (!string.IsNullOrWhiteSpace(game.Started))
            {
                notes.Add("keptStarted");
            }

            if (proposedStarted == null && !shouldSuggestFinished)
                continue;

            suggestions.Add(new SteamDateSuggestionDto
            {
                GameId = game.Id,
                GameName = game.Name,
                SteamAppId = appId,
                SteamName = ownedGame?.Name ?? appDetails?.Name ?? game.Name,
                SteamIconUrl = ownedGame?.IconUrl ?? appDetails?.HeaderImageUrl,
                SteamPlaytimeForever = ownedGame?.PlaytimeForever,
                CurrentStarted = game.Started,
                CurrentFinished = game.Finished,
                ProposedStarted = proposedStarted,
                ProposedFinished = shouldSuggestFinished ? proposedFinished : null,
                StartedSource = startedSource,
                FinishedSource = finishedSource,
                IsFinishedConflict = isFinishedConflict,
                IsFinishedSteamManaged = isSteamManagedFinished,
                Notes = notes
            });
        }

        return Ok(suggestions);
    }

    [HttpPost("date-suggestions/apply")]
    public async Task<IActionResult> ApplyDateSuggestions([FromBody] SteamApplyDateSuggestionsRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var response = new SteamApplyDateSuggestionsResponse();
        var requested = request.Suggestions
            .Where(s => s.GameId > 0 && (!string.IsNullOrWhiteSpace(s.Started) || !string.IsNullOrWhiteSpace(s.Finished)))
            .GroupBy(s => s.GameId)
            .Select(g => g.First())
            .ToList();

        if (requested.Count == 0)
            return BadRequest(new { message = "No date suggestions provided" });

        var gameIds = requested.Select(s => s.GameId).ToList();
        var games = await _context.Games
            .Where(g => g.UserId == userId.Value && gameIds.Contains(g.Id) && g.SteamAppId.HasValue)
            .ToDictionaryAsync(g => g.Id, g => g);

        foreach (var suggestion in requested)
        {
            if (!games.TryGetValue(suggestion.GameId, out var game))
            {
                response.Errors.Add($"gameNotFound:{suggestion.GameId}");
                continue;
            }

            var updated = false;
            if (string.IsNullOrWhiteSpace(game.Started) && IsValidDateValue(suggestion.Started))
            {
                game.Started = suggestion.Started;
                updated = true;
            }

            if (string.IsNullOrWhiteSpace(game.Finished) && IsValidDateValue(suggestion.Finished))
            {
                game.Finished = suggestion.Finished;
                game.SteamFinishedSource = "steam";
                game.SteamFinishedLastValue = suggestion.Finished;
                game.SteamFinishedSyncedAt = DateTime.UtcNow;
                if (game.SteamFinishedRejectedValue == suggestion.Finished)
                    game.SteamFinishedRejectedValue = null;
                updated = true;
            }
            else if (IsValidDateValue(suggestion.Finished) && game.Finished != suggestion.Finished)
            {
                game.Finished = suggestion.Finished;
                game.SteamFinishedSource = "steam";
                game.SteamFinishedLastValue = suggestion.Finished;
                game.SteamFinishedSyncedAt = DateTime.UtcNow;
                if (game.SteamFinishedRejectedValue == suggestion.Finished)
                    game.SteamFinishedRejectedValue = null;
                updated = true;
            }

            if (updated)
            {
                game.CalculateScore();
                response.Updated++;
            }
        }

        if (response.Updated > 0)
            await _context.SaveChangesAsync();

        return Ok(response);
    }

    [HttpPost("date-suggestions/dismiss")]
    public async Task<IActionResult> DismissDateSuggestions([FromBody] SteamDismissDateSuggestionsRequest request)
    {
        var userId = CurrentUserId;
        if (!userId.HasValue) return Unauthorized();

        var requested = request.Suggestions
            .Where(s => s.GameId > 0 && IsValidDateValue(s.Finished))
            .GroupBy(s => s.GameId)
            .Select(g => g.First())
            .ToList();

        if (requested.Count == 0)
            return BadRequest(new { message = "No date suggestions provided" });

        var gameIds = requested.Select(s => s.GameId).ToList();
        var games = await _context.Games
            .Where(g => g.UserId == userId.Value && gameIds.Contains(g.Id) && g.SteamAppId.HasValue)
            .ToDictionaryAsync(g => g.Id, g => g);

        var dismissed = 0;
        foreach (var suggestion in requested)
        {
            if (!games.TryGetValue(suggestion.GameId, out var game))
                continue;

            if (game.SteamFinishedRejectedValue == suggestion.Finished)
                continue;

            game.SteamFinishedRejectedValue = suggestion.Finished;
            dismissed++;
        }

        if (dismissed > 0)
            await _context.SaveChangesAsync();

        return Ok(new { dismissed });
    }

    private static string MatchKey(int steamAppId, int gameId) => $"{steamAppId}:{gameId}";

    private sealed record MatchGameCandidate(int Id, string Name, int? SteamAppId);

    private static SteamMatchSuggestionDto? FindBestMatchSuggestion(
        int steamAppId,
        string steamName,
        string? steamIconUrl,
        IEnumerable<MatchGameCandidate> gdbGames,
        HashSet<string> dismissedPairs)
    {
        SteamMatchSuggestionDto? best = null;

        foreach (var gg in gdbGames)
        {
            var suggestion = FindMatchSuggestion(steamAppId, steamName, steamIconUrl, gg.Id, gg.Name, dismissedPairs);
            if (suggestion != null && (best == null || suggestion.Confidence > best.Confidence))
                best = suggestion;
        }

        return best;
    }

    private static SteamMatchSuggestionDto? FindMatchSuggestion(
        int steamAppId,
        string steamName,
        string? steamIconUrl,
        int gdbGameId,
        string gdbGameName,
        HashSet<string> dismissedPairs)
    {
        if (dismissedPairs.Contains(MatchKey(steamAppId, gdbGameId)))
            return null;

        var score = NameScore(steamName, gdbGameName);
        if (score < 50)
            return null;

        return new SteamMatchSuggestionDto
        {
            SteamAppId = steamAppId,
            SteamName = steamName,
            SteamIconUrl = steamIconUrl,
            GdbGameId = gdbGameId,
            GdbGameName = gdbGameName,
            Confidence = score
        };
    }

    private static string? ExtractSteamId(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var match = Regex.Match(input.Trim(), @"\b\d{17}\b");
        return match.Success ? match.Value : null;
    }

    private static int NameScore(string a, string b)
    {
        a = NormalizeName(a);
        b = NormalizeName(b);

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

    private static string NormalizeName(string name)
    {
        var normalized = name.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
        var chars = normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();

        return Regex.Replace(new string(chars), @"\s+", " ").Trim();
    }

    private async Task<DateTime?> GetFirstAchievementUnlockAsync(int userId, string steamId, int appId)
    {
        var cacheKey = $"steam:first-achievement:{userId}:{appId}";
        if (_cache.TryGetValue<DateTime?>(cacheKey, out var cachedUnlock))
            return cachedUnlock;

        var cached = await _context.SteamAchievements
            .Where(a => a.UserId == userId && a.SteamAppId == appId && a.Achieved && a.UnlockTime.HasValue)
            .OrderBy(a => a.UnlockTime)
            .Select(a => a.UnlockTime)
            .FirstOrDefaultAsync();

        if (cached.HasValue)
        {
            _cache.Set(cacheKey, cached.Value, TimeSpan.FromHours(12));
            return cached.Value;
        }

        var achievements = await _steamApi.GetPlayerAchievementsAsync(steamId, appId);
        if (!achievements.Success)
        {
            _cache.Set<DateTime?>(cacheKey, null, TimeSpan.FromHours(2));
            return null;
        }

        var firstUnlockUnix = achievements.Achievements
            .Where(a => a.Achieved == 1 && a.UnlockTime > 0)
            .Select(a => a.UnlockTime)
            .DefaultIfEmpty(0)
            .Min();

        DateTime? firstUnlock = firstUnlockUnix > 0
            ? DateTimeOffset.FromUnixTimeSeconds(firstUnlockUnix).UtcDateTime
            : null;
        _cache.Set(cacheKey, firstUnlock, firstUnlock.HasValue ? TimeSpan.FromHours(12) : TimeSpan.FromHours(2));
        return firstUnlock;
    }

    private Task<List<SteamOwnedGameDto>> GetCachedOwnedGamesAsync(string steamId) =>
        _cache.GetOrCreateAsync($"steam:owned:{steamId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            return await _steamApi.GetOwnedGamesAsync(steamId);
        })!;

    private Task<List<SteamStoreSearchItemDto>> GetCachedStoreSearchAsync(string query)
    {
        var key = $"steam:store-search:{NormalizeName(query)}";
        return _cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12);
            return await _steamStore.SearchStoreAsync(query);
        })!;
    }

    private static bool IsValidDateValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
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

        var result = await _steamSync.AddStoreGameAsync(userId.Value, request.AppId, request.LogoUrl, request.CoverUrl);
        return Ok(result);
    }
}
