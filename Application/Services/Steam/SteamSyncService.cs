using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services.Steam;

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

            // Only update critic score if not already set (preserve user modifications)
            if (game.Critic == null)
                await UpdateSteamCriticScoreAsync(game, game.SteamAppId.Value);

            // Update playtime from bulk call
            if (playtimeMap.TryGetValue(game.SteamAppId.Value, out var steamGame))
            {
                game.SteamPlaytimeForever = steamGame.PlaytimeForever;
                game.SteamPlaytime2Weeks = steamGame.Playtime2Weeks;
                SyncSteamManagedFinishedDate(game, steamGame);
                gamesUpdated++;
            }

            await FillMissingSteamImagesAsync(game, playtimeMap.GetValueOrDefault(game.SteamAppId.Value));
            NormalizeStoredSteamReleaseDate(game);

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

    public async Task<SteamImportResult> ImportLibraryAsync(int userId, SteamImportRequest request)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user?.SteamId == null)
            return new SteamImportResult { Success = false, Error = "User has no Steam account linked" };

        var importItems = NormalizeImportItems(request);
        var appIds = importItems.Keys.ToList();

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
            var importItem = importItems[appId];

            if (existingByAppId.TryGetValue(appId, out var existingGame))
            {
                // Preserve user-curated fields (PlatformId, Critic, Cover, Logo, etc.).
                // Only fill critic score if the game doesn't already have one.
                if (existingGame.Critic == null)
                    await UpdateSteamCriticScoreAsync(existingGame, appId);

                // Already linked - update playtime
                if (ownedByAppId.TryGetValue(appId, out var owned))
                {
                    existingGame.SteamPlaytimeForever = owned.PlaytimeForever;
                    existingGame.SteamPlaytime2Weeks = owned.Playtime2Weeks;
                    existingGame.SteamLastSynced = DateTime.UtcNow;
                }
                await FillMissingSteamImagesAsync(existingGame, ownedByAppId.GetValueOrDefault(appId), importItem);
                NormalizeStoredSteamReleaseDate(existingGame);
                await TrySyncAchievementsForImportedGameAsync(user.SteamId, existingGame);
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

            if (!request.CreateMissing)
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
            var coverUrl = appDetails?.HeaderImageUrl ?? importItem.CoverUrl ?? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

            var (criticScore, criticProvider) = await ResolveSteamCriticScoreAsync(appId, appDetails);

            // Community icon (store page scraping) first; fall back to owned-game API hash icon
            string? resolvedLogoUrl = await _steamStore.GetCommunityIconUrlAsync(appId);
            if (string.IsNullOrWhiteSpace(resolvedLogoUrl))
                resolvedLogoUrl = ownedGame?.IconUrl;
            if (string.IsNullOrWhiteSpace(resolvedLogoUrl))
                resolvedLogoUrl = importItem.LogoUrl;

            var newGame = CreateSteamGameEntity(
                userId,
                defaultStatus.Id,
                gameName,
                appId,
                appDetails,
                criticScore,
                criticProvider,
                steamPlatform?.Id,
                coverUrl,
                resolvedLogoUrl ?? GetSteamLogoUrl(appId, coverUrl));
            newGame.SteamPlaytimeForever = ownedGame?.PlaytimeForever;
            newGame.SteamPlaytime2Weeks = ownedGame?.Playtime2Weeks;
            newGame.SteamLastSynced = DateTime.UtcNow;

            _context.Games.Add(newGame);
            await _context.SaveChangesAsync();
            await TrySyncAchievementsForImportedGameAsync(user.SteamId, newGame);

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

    private async Task<int> TrySyncAchievementsForImportedGameAsync(string steamId, Game game)
    {
        try
        {
            return await SyncAchievementsForGameAsync(steamId, game);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to sync Steam achievements during import for game {GameId} / app {SteamAppId}", game.Id, game.SteamAppId);
            return 0;
        }
    }

    public async Task<SteamImportedGameDto> AddStoreGameAsync(int userId, int appId, string? logoUrl = null, string? coverUrl = null)
    {
        // Check if already exists in GDB
        var existing = await _context.Games
            .FirstOrDefaultAsync(g => g.UserId == userId && g.SteamAppId == appId);
        if (existing != null)
            return new SteamImportedGameDto { AppId = appId, Name = existing.Name, GdbGameId = existing.Id, Action = "exists" };

        // Get default status (prefer NotFulfilled)
        var defaultStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.UserId == userId && s.StatusType == SpecialStatusType.NotFulfilled && s.IsDefault);
        defaultStatus ??= await _context.GameStatuses.FirstOrDefaultAsync(s => s.UserId == userId);
        if (defaultStatus == null)
            return new SteamImportedGameDto { AppId = appId, Name = $"App {appId}", Action = "error" };

        // Fetch store details
        var appDetails = await _steamStore.GetOrCacheAppDetailsAsync(appId);
        var gameName = appDetails?.Name ?? $"Steam App {appId}";
        var resolvedCoverUrl = appDetails?.HeaderImageUrl ?? coverUrl ?? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

        // Find Steam platform
        var steamPlatform = await _context.GamePlatforms
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Name.ToLower() == "steam");

        // Critic score
        var (criticScore, criticProvider) = await ResolveSteamCriticScoreAsync(appId, appDetails);

        // Community icon (store page scraping) first; fall back to owned-game hash; then caller URL
        string? resolvedLogoUrl = await _steamStore.GetCommunityIconUrlAsync(appId);
        if (string.IsNullOrWhiteSpace(resolvedLogoUrl))
        {
            var userForIcon = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
            if (!string.IsNullOrWhiteSpace(userForIcon?.SteamId))
            {
                var ownedIconUrl = (await _steamApi.GetOwnedGamesAsync(userForIcon.SteamId))
                    .FirstOrDefault(g => g.AppId == appId)?.IconUrl;
                if (!string.IsNullOrWhiteSpace(ownedIconUrl))
                    resolvedLogoUrl = ownedIconUrl;
            }
        }
        if (string.IsNullOrWhiteSpace(resolvedLogoUrl))
            resolvedLogoUrl = logoUrl;

        var newGame = CreateSteamGameEntity(
            userId,
            defaultStatus.Id,
            gameName,
            appId,
            appDetails,
            criticScore,
            criticProvider,
            steamPlatform?.Id,
            resolvedCoverUrl,
            logoUrl: resolvedLogoUrl ?? GetSteamLogoUrl(appId, resolvedCoverUrl));

        _context.Games.Add(newGame);
        await _context.SaveChangesAsync();

        // Sync playtime and achievements immediately (also re-links any orphaned achievements from a prior delete)
        var user = await _context.Users.FindAsync(userId);
        if (user?.SteamId != null)
            await SyncGameInternalAsync(user, newGame);

        return new SteamImportedGameDto { AppId = appId, Name = gameName, GdbGameId = newGame.Id, Action = "created" };
    }

    private static Game CreateSteamGameEntity(
        int userId,
        int statusId,
        string gameName,
        int appId,
        SteamAppDetailsDto? appDetails,
        int? criticScore,
        string? criticProvider,
        int? platformId,
        string? coverUrl,
        string? logoUrl)
    {
        var createDto = new GameCreateDto
        {
            Name = gameName,
            StatusId = statusId,
            Critic = criticScore,
            CriticProvider = criticProvider,
            PlatformId = platformId,
            Released = GameDateNormalizer.NormalizeSteamReleaseDate(appDetails?.ReleaseDate),
            PlayWithIds = [],
            Logo = logoUrl,
            Cover = coverUrl,
            SteamAppId = appId
        };

        var game = createDto.ToEntity();
        game.UserId = userId;
        return game;
    }

    private static void NormalizeStoredSteamReleaseDate(Game game)
    {
        var normalizedReleaseDate = GameDateNormalizer.NormalizeSteamReleaseDate(game.Released);
        if (normalizedReleaseDate != null && game.Released != normalizedReleaseDate)
        {
            game.Released = normalizedReleaseDate;
        }
    }

    private static string GetSteamLogoUrl(int appId, string? headerImageUrl = null)
    {
        if (!string.IsNullOrWhiteSpace(headerImageUrl))
        {
            var logoFromHeader = headerImageUrl.Replace("/header.jpg", "/logo.png", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(logoFromHeader, headerImageUrl, StringComparison.Ordinal))
                return logoFromHeader;
        }

        return $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/logo.png";
    }

    private static bool ShouldReplaceWithCommunityIcon(string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return true;
        // Community icon (hash-based jpg from steamcommunity) is what we want — keep it
        if (logoUrl.Contains("steamcommunity/public/images/apps/", StringComparison.OrdinalIgnoreCase)) return false;

        // logo.png, header.jpg and other store assets are NOT icons — replace them
        return logoUrl.Contains("store_item_assets/steam/apps/", StringComparison.OrdinalIgnoreCase)
            || logoUrl.Contains("/steam/apps/", StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<int, SteamImportGameRequest> NormalizeImportItems(SteamImportRequest request)
    {
        var importItems = new Dictionary<int, SteamImportGameRequest>();

        foreach (var game in (request.Games ?? []).Where(game => game.AppId > 0))
        {
            importItems[game.AppId] = game;
        }

        foreach (var appId in (request.AppIds ?? []).Where(appId => appId > 0))
        {
            importItems.TryAdd(appId, new SteamImportGameRequest { AppId = appId });
        }

        return importItems;
    }

    private async Task FillMissingSteamImagesAsync(Game game, SteamOwnedGameDto? ownedGame = null, SteamImportGameRequest? importItem = null)
    {
        if (game.SteamAppId == null) return;

        var appId = game.SteamAppId.Value;
        var shouldResolveLogo = ShouldReplaceWithCommunityIcon(game.Logo);
        var shouldResolveCover = string.IsNullOrWhiteSpace(game.Cover);

        if (!shouldResolveCover && !shouldResolveLogo) return;

        // Try the community icon from the store page (actual game icon, best quality available)
        if (shouldResolveLogo)
        {
            var communityIconUrl = await _steamStore.GetCommunityIconUrlAsync(appId);
            if (!string.IsNullOrWhiteSpace(communityIconUrl))
            {
                game.Logo = communityIconUrl;
                shouldResolveLogo = false;
            }
            else if (!string.IsNullOrWhiteSpace(ownedGame?.IconUrl))
            {
                game.Logo = ownedGame.IconUrl;
                shouldResolveLogo = false;
            }
        }

        if (!shouldResolveCover && !shouldResolveLogo) return;

        var appDetails = await _steamStore.GetOrCacheAppDetailsAsync(appId);
        var coverUrl = appDetails?.HeaderImageUrl ?? importItem?.CoverUrl ?? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

        if (shouldResolveCover)
            game.Cover = coverUrl;

        if (shouldResolveLogo)
            game.Logo = !string.IsNullOrWhiteSpace(importItem?.LogoUrl) ? importItem.LogoUrl : GetSteamLogoUrl(appId, coverUrl);
    }

    private async Task<SteamSyncResult> SyncGameInternalAsync(User user, Game game)
    {
        if (user.SteamId == null || game.SteamAppId == null)
            return new SteamSyncResult { Success = false };

        // Only update critic score if not already set (preserve user modifications)
        if (game.Critic == null)
            await UpdateSteamCriticScoreAsync(game, game.SteamAppId.Value);

        // Get owned games for playtime
        var ownedGames = await _steamApi.GetOwnedGamesAsync(user.SteamId);
        var ownedGame = ownedGames.FirstOrDefault(g => g.AppId == game.SteamAppId.Value);

        if (ownedGame != null)
        {
            game.SteamPlaytimeForever = ownedGame.PlaytimeForever;
            game.SteamPlaytime2Weeks = ownedGame.Playtime2Weeks;
            SyncSteamManagedFinishedDate(game, ownedGame);
        }

        await FillMissingSteamImagesAsync(game, ownedGame);
        NormalizeStoredSteamReleaseDate(game);

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
        var schemaMap = schema?.Achievements
            .GroupBy(a => a.ApiName)
            .ToDictionary(g => g.Key, g => g.First())
            ?? new Dictionary<string, SteamSchemaAchievement>();
        var playerMap = playerAch.Achievements
            .GroupBy(a => a.ApiName)
            .ToDictionary(g => g.Key, g => g.First());
        var achievementApiNames = schemaMap.Count > 0
            ? schemaMap.Keys.ToList()
            : playerMap.Keys.ToList();

        var userId = game.UserId;
        var existingAch = await _context.SteamAchievements
            .Where(a => a.UserId == userId && a.SteamAppId == game.SteamAppId.Value)
            .ToDictionaryAsync(a => a.ApiName, a => a);

        int count = 0;
        foreach (var apiName in achievementApiNames)
        {
            schemaMap.TryGetValue(apiName, out var schemaDef);
            playerMap.TryGetValue(apiName, out var raw);
            var achieved = raw?.Achieved == 1;
            var unlockTime = raw?.UnlockTime > 0
                ? DateTimeOffset.FromUnixTimeSeconds(raw.UnlockTime).UtcDateTime
                : (DateTime?)null;

            if (existingAch.TryGetValue(apiName, out var existing))
            {
                var changed =
                    existing.GameId != game.Id ||
                    existing.Achieved != achieved ||
                    existing.UnlockTime != unlockTime ||
                    (schemaDef != null && (
                        existing.DisplayName != schemaDef.DisplayName ||
                        existing.Description != schemaDef.Description ||
                        existing.IconUrl != schemaDef.Icon ||
                        existing.IconGrayUrl != schemaDef.IconGray ||
                        existing.Hidden != (schemaDef.Hidden == 1)));

                // Re-link to the current game (GameId may be NULL if the game was deleted and re-added)
                existing.GameId = game.Id;
                existing.Achieved = achieved;
                existing.UnlockTime = unlockTime;
                existing.LastSynced = DateTime.UtcNow;
                if (schemaDef != null)
                {
                    existing.DisplayName = schemaDef.DisplayName;
                    existing.Description = schemaDef.Description;
                    existing.IconUrl = schemaDef.Icon;
                    existing.IconGrayUrl = schemaDef.IconGray;
                    existing.Hidden = schemaDef.Hidden == 1;
                }

                if (changed)
                    count++;
            }
            else
            {
                var newAch = new SteamAchievement
                {
                    UserId = userId,
                    GameId = game.Id,
                    SteamAppId = game.SteamAppId.Value,
                    ApiName = apiName,
                    Achieved = achieved,
                    UnlockTime = unlockTime,
                    DisplayName = schemaDef?.DisplayName ?? apiName,
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

    private static void SyncSteamManagedFinishedDate(Game game, SteamOwnedGameDto ownedGame)
    {
        if (game.SteamFinishedSource != "steam" || !ownedGame.LastPlayedAt.HasValue)
            return;

        var steamFinished = ownedGame.LastPlayedAt.Value.ToString("yyyy-MM-dd");
        if (game.Finished == steamFinished && game.SteamFinishedLastValue == steamFinished)
            return;

        game.Finished = steamFinished;
        game.SteamFinishedLastValue = steamFinished;
        game.SteamFinishedSyncedAt = DateTime.UtcNow;
        if (game.SteamFinishedRejectedValue == steamFinished)
            game.SteamFinishedRejectedValue = null;
    }

    private async Task UpdateSteamCriticScoreAsync(Game game, int appId, SteamAppDetailsDto? appDetails = null)
    {
        var (criticScore, criticProvider) = await ResolveSteamCriticScoreAsync(appId, appDetails);
        game.Critic = criticScore;
        game.CriticProvider = criticProvider;
    }

    private async Task<(int? CriticScore, string? CriticProvider)> ResolveSteamCriticScoreAsync(int appId, SteamAppDetailsDto? appDetails = null)
    {
        var resolvedAppDetails = appDetails ?? await _steamStore.GetOrCacheAppDetailsAsync(appId);

        // Always prioritize Metacritic when available.
        if (resolvedAppDetails?.MetacriticScore is int metacriticScore)
        {
            return (metacriticScore, "Metacritic");
        }

        var reviews = await _steamStore.GetReviewSummaryAsync(appId);
        if (reviews != null && reviews.TotalReviews >= 10)
        {
            return (reviews.ScorePercent, "SteamDB");
        }

        return (null, null);
    }
}
