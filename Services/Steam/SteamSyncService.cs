using GamesDatabase.Api.Data;
using GamesDatabase.Api.DTOs.Steam;
using GamesDatabase.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Services.Steam;

public class SteamSyncService : ISteamSyncService
{
    private readonly GamesDbContext _context;
    private readonly ISteamApiService _steamApi;
    private readonly ISteamStoreService _steamStore;
    private readonly ILogger<SteamSyncService> _logger;

    public SteamSyncService(GamesDbContext context, ISteamApiService steamApi, ISteamStoreService steamStore, ILogger<SteamSyncService> logger)
    {
        _context = context;
        _steamApi = steamApi;
        _steamStore = steamStore;
        _logger = logger;
    }

    public async Task<SteamSyncResult> SyncGameAsync(int userId, int gameId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user?.SteamId == null)
            return new SteamSyncResult { Success = false, Error = "User has no Steam account linked" };

        var game = await _context.Games.FindAsync(gameId);
        if (game == null || game.UserId != userId)
            return new SteamSyncResult { Success = false, Error = "Game not found" };

        if (game.SteamAppId == null)
            return new SteamSyncResult { Success = false, Error = "Game has no Steam App ID linked" };

        return await SyncGameInternalAsync(user, game);
    }

    public async Task<SteamSyncResult> SyncAllUserGamesAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user?.SteamId == null)
            return new SteamSyncResult { Success = false, Error = "User has no Steam account linked" };

        var games = await _context.Games
            .Where(g => g.UserId == userId && g.SteamAppId != null)
            .ToListAsync();

        if (games.Count == 0)
            return new SteamSyncResult { Success = true };

        // Get all owned games once to update playtime
        var ownedGames = await _steamApi.GetOwnedGamesAsync(user.SteamId);
        var playtimeMap = ownedGames.ToDictionary(g => g.AppId, g => g);

        int gamesUpdated = 0;
        int achievementsUpdated = 0;

        foreach (var game in games)
        {
            if (game.SteamAppId == null) continue;

            // Update playtime from bulk call
            if (playtimeMap.TryGetValue(game.SteamAppId.Value, out var steamGame))
            {
                game.SteamPlaytimeForever = steamGame.PlaytimeForever;
                game.SteamPlaytime2Weeks = steamGame.Playtime2Weeks;
                gamesUpdated++;
            }

            // Sync achievements
            var achResult = await SyncAchievementsForGameAsync(user.SteamId, game);
            achievementsUpdated += achResult;

            game.SteamLastSynced = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();

        return new SteamSyncResult
        {
            Success = true,
            GamesUpdated = gamesUpdated,
            AchievementsUpdated = achievementsUpdated
        };
    }

    public async Task<SteamImportResult> ImportLibraryAsync(int userId, List<int> appIds, bool createMissing)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user?.SteamId == null)
            return new SteamImportResult { Success = false, Error = "User has no Steam account linked" };

        // Get existing games for this user that have a SteamAppId
        var existingByAppId = await _context.Games
            .Where(g => g.UserId == userId && g.SteamAppId != null && appIds.Contains(g.SteamAppId!.Value))
            .ToDictionaryAsync(g => g.SteamAppId!.Value, g => g);

        // Get owned games for playtime
        var ownedGames = await _steamApi.GetOwnedGamesAsync(user.SteamId);
        var ownedByAppId = ownedGames.ToDictionary(g => g.AppId, g => g);

        // Get default status
        var defaultStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.UserId == userId && s.StatusType == SpecialStatusType.NotFulfilled && s.IsDefault);
        defaultStatus ??= await _context.GameStatuses.FirstOrDefaultAsync(s => s.UserId == userId);

        // Find Steam platform for this user (case-insensitive)
        var steamPlatform = await _context.GamePlatforms
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Name.ToLower() == "steam");

        var result = new SteamImportResult { Success = true };

        foreach (var appId in appIds)
        {
            if (existingByAppId.TryGetValue(appId, out var existingGame))
            {
                // Already linked - update playtime
                if (ownedByAppId.TryGetValue(appId, out var owned))
                {
                    existingGame.SteamPlaytimeForever = owned.PlaytimeForever;
                    existingGame.SteamPlaytime2Weeks = owned.Playtime2Weeks;
                    existingGame.SteamLastSynced = DateTime.UtcNow;
                }
                result.Linked++;
                result.ImportedGames.Add(new SteamImportedGameDto
                {
                    AppId = appId,
                    Name = existingGame.Name,
                    GdbGameId = existingGame.Id,
                    Action = "linked"
                });
                continue;
            }

            if (!createMissing)
            {
                result.Skipped++;
                result.ImportedGames.Add(new SteamImportedGameDto { AppId = appId, Name = $"App {appId}", Action = "skipped" });
                continue;
            }

            // Fetch store details to create game
            var appDetails = await _steamStore.GetOrCacheAppDetailsAsync(appId);
            var ownedGame = ownedByAppId.TryGetValue(appId, out var og) ? og : null;
            var gameName = appDetails?.Name ?? ownedGame?.Name ?? $"Steam App {appId}";

            if (defaultStatus == null)
            {
                result.Skipped++;
                continue;
            }

            // Use header image from store cache, fall back to standard CDN URL
            var coverUrl = appDetails?.HeaderImageUrl ?? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

            // Determine critic score: prefer Metacritic, fall back to Steam review percentage
            int? criticScore = appDetails?.MetacriticScore;
            string? criticProvider = criticScore.HasValue ? "Metacritic" : null;

            if (!criticScore.HasValue)
            {
                var reviews = await _steamStore.GetReviewSummaryAsync(appId);
                if (reviews != null && reviews.TotalReviews >= 10)
                {
                    criticScore = reviews.ScorePercent;
                    criticProvider = "SteamDB";
                }
            }

            var newGame = new Game
            {
                Name = gameName,
                StatusId = defaultStatus.Id,
                UserId = userId,
                SteamAppId = appId,
                SteamPlaytimeForever = ownedGame?.PlaytimeForever,
                SteamPlaytime2Weeks = ownedGame?.Playtime2Weeks,
                SteamLastSynced = DateTime.UtcNow,
                Released = appDetails?.ReleaseDate,
                Logo = ownedGame?.IconUrl,
                Cover = coverUrl,
                Critic = criticScore,
                CriticProvider = criticProvider,
                PlatformId = steamPlatform?.Id
            };

            newGame.CalculateScore();
            _context.Games.Add(newGame);
            await _context.SaveChangesAsync();

            result.Created++;
            result.ImportedGames.Add(new SteamImportedGameDto
            {
                AppId = appId,
                Name = gameName,
                GdbGameId = newGame.Id,
                Action = "created"
            });
        }

        await _context.SaveChangesAsync();
        return result;
    }

    private async Task<SteamSyncResult> SyncGameInternalAsync(User user, Game game)
    {
        if (user.SteamId == null || game.SteamAppId == null)
            return new SteamSyncResult { Success = false };

        // Get owned games for playtime
        var ownedGames = await _steamApi.GetOwnedGamesAsync(user.SteamId);
        var ownedGame = ownedGames.FirstOrDefault(g => g.AppId == game.SteamAppId.Value);

        if (ownedGame != null)
        {
            game.SteamPlaytimeForever = ownedGame.PlaytimeForever;
            game.SteamPlaytime2Weeks = ownedGame.Playtime2Weeks;
        }

        var achCount = await SyncAchievementsForGameAsync(user.SteamId, game);
        game.SteamLastSynced = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return new SteamSyncResult
        {
            Success = true,
            GamesUpdated = 1,
            AchievementsUpdated = achCount
        };
    }

    private async Task<int> SyncAchievementsForGameAsync(string steamId, Game game)
    {
        if (game.SteamAppId == null) return 0;

        var playerAch = await _steamApi.GetPlayerAchievementsAsync(steamId, game.SteamAppId.Value);
        if (!playerAch.Success || playerAch.Achievements.Count == 0) return 0;

        var schema = await _steamApi.GetGameSchemaAsync(game.SteamAppId.Value);
        var schemaMap = schema?.Achievements.ToDictionary(a => a.ApiName, a => a) ?? new Dictionary<string, SteamSchemaAchievement>();

        var userId = game.UserId;
        var existingAch = await _context.SteamAchievements
            .Where(a => a.UserId == userId && a.SteamAppId == game.SteamAppId.Value)
            .ToDictionaryAsync(a => a.ApiName, a => a);

        int count = 0;
        foreach (var raw in playerAch.Achievements)
        {
            schemaMap.TryGetValue(raw.ApiName, out var schemaDef);

            if (existingAch.TryGetValue(raw.ApiName, out var existing))
            {
                existing.Achieved = raw.Achieved == 1;
                existing.UnlockTime = raw.UnlockTime > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(raw.UnlockTime).UtcDateTime
                    : null;
                existing.LastSynced = DateTime.UtcNow;
                if (schemaDef != null)
                {
                    existing.DisplayName = schemaDef.DisplayName;
                    existing.Description = schemaDef.Description;
                    existing.IconUrl = schemaDef.Icon;
                    existing.IconGrayUrl = schemaDef.IconGray;
                    existing.Hidden = schemaDef.Hidden == 1;
                }
            }
            else
            {
                var newAch = new SteamAchievement
                {
                    UserId = userId,
                    GameId = game.Id,
                    SteamAppId = game.SteamAppId.Value,
                    ApiName = raw.ApiName,
                    Achieved = raw.Achieved == 1,
                    UnlockTime = raw.UnlockTime > 0
                        ? DateTimeOffset.FromUnixTimeSeconds(raw.UnlockTime).UtcDateTime
                        : null,
                    DisplayName = schemaDef?.DisplayName ?? raw.ApiName,
                    Description = schemaDef?.Description,
                    IconUrl = schemaDef?.Icon,
                    IconGrayUrl = schemaDef?.IconGray,
                    Hidden = schemaDef?.Hidden == 1,
                    LastSynced = DateTime.UtcNow
                };
                _context.SteamAchievements.Add(newAch);
                count++;
            }
        }

        return count;
    }
}
