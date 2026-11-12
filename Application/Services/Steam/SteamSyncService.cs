using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;

namespace GamesDatabase.Api.Application.Services.Steam;

public class SteamSyncService : ISteamSyncService
{
    // Focus, the Steam tab and manual bulk sync may overlap. Hold this user-scoped
    // gate through SaveChanges so another scope cannot insert the same cache rows
    // or overwrite a newly preserved completion using an older tracked Game.
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> _syncGates = new();
    private readonly GamesDbContext _context;
    private readonly ISteamApiService _steamApi;
    private readonly ISteamStoreService _steamStore;
    private readonly ILogger<SteamSyncService> _logger;
    private readonly SteamAchievementSyncService _achievementSync;

    public SteamSyncService(GamesDbContext context, ISteamApiService steamApi, ISteamStoreService steamStore, ILogger<SteamSyncService> logger, SteamAchievementSyncService achievementSync)
    {
        _context = context;
        _steamApi = steamApi;
        _steamStore = steamStore;
        _logger = logger;
        _achievementSync = achievementSync;
    }

    private static string GetSteamLibraryCoverUrl(int appId) => $"https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900.jpg";

    private static bool IsLegacySteamAssetUrl(string? url) =>
        !string.IsNullOrWhiteSpace(url) &&
        (url.Contains("/steam/apps/", StringComparison.OrdinalIgnoreCase) &&
         (url.EndsWith("/header.jpg", StringComparison.OrdinalIgnoreCase) ||
          url.EndsWith("/library_600x900.jpg", StringComparison.OrdinalIgnoreCase)));

    public async Task<SteamSyncResult> SyncGameAsync(int userId, int gameId)
    {
        using var syncGate = await AcquireSyncGateAsync(userId);
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

    public async Task<SteamSyncResult> SyncAllUserGamesAsync(int userId, Action<SteamSyncProgress>? onProgress = null, CancellationToken cancellationToken = default)
    {
        using var syncGate = await AcquireSyncGateAsync(userId, cancellationToken);
        var user = await _context.Users.FindAsync([userId], cancellationToken);
        if (user?.SteamId == null)
            return new SteamSyncResult { Success = false, Error = "User has no Steam account linked" };
        var steamId = user.SteamId;
        var gameIds = await _context.Games.AsNoTracking()
            .Where(game => game.UserId == userId && game.SteamAppId != null)
            .OrderBy(game => game.Id).Select(game => game.Id).ToListAsync(cancellationToken);
        var result = new SteamSyncResult { Success = true, TotalGames = gameIds.Count };
        void Report(string? name = null) => onProgress?.Invoke(new SteamSyncProgress
        {
            TotalGames = result.TotalGames, ProcessedGames = result.ProcessedGames,
            GamesUpdated = result.GamesUpdated, AchievementsUpdated = result.AchievementsUpdated,
            FailedGames = result.FailedGames, WarningCount = result.WarningCount, CurrentGameName = name
        });
        void Warn(int gameId, string code)
        {
            result.WarningCount++;
            if (result.Warnings.Count < 50) result.Warnings.Add($"{gameId}:{code}");
        }
        Report();
        if (gameIds.Count == 0) return result;

        // Fetch playtime once; do not retain 1000+ tracked games until one final commit.
        var ownedGames = await _steamApi.GetOwnedGamesAsync(steamId);
        var playtimeMap = ownedGames.GroupBy(game => game.AppId).ToDictionary(group => group.Key, group => group.First());
        _context.ChangeTracker.Clear();
        foreach (var gameId in gameIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var game = await _context.Games.FirstOrDefaultAsync(game => game.Id == gameId && game.UserId == userId, cancellationToken);
            if (game?.SteamAppId == null)
            {
                result.ProcessedGames++;
                _context.ChangeTracker.Clear();
                Report();
                continue;
            }
            Report(game.Name);
            try
            {
                if (game.Critic == null) await UpdateSteamCriticScoreAsync(game, game.SteamAppId.Value);
                var hasPlaytime = playtimeMap.TryGetValue(game.SteamAppId.Value, out var steamGame);
                if (steamGame != null)
                {
                    game.SteamPlaytimeForever = steamGame.PlaytimeForever;
                    game.SteamPlaytime2Weeks = steamGame.Playtime2Weeks;
                    SyncSteamManagedFinishedDate(game, steamGame);
                }
                await FillMissingSteamImagesAsync(game, steamGame);
                NormalizeStoredSteamReleaseDate(game);
                var achievementCount = await SyncAchievementsForGameAsync(steamId, game);
                cancellationToken.ThrowIfCancellationRequested();
                // A link changed during a long sync must not receive the old account's data.
                var currentLink = await _context.Games.AsNoTracking().Where(current => current.Id == gameId && current.UserId == userId)
                    .Select(current => new { current.SteamAppId, SteamId = current.User.SteamId }).FirstOrDefaultAsync(cancellationToken);
                if (currentLink == null || currentLink.SteamAppId != game.SteamAppId)
                    throw new InvalidOperationException("Steam game link changed before commit");
                if (currentLink.SteamId != steamId)
                {
                    _context.ChangeTracker.Clear();
                    result.Success = false;
                    result.Error = "steamAccountChanged";
                    return result;
                }
                game.SteamLastSynced = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
                // Report committed work, never merely fetched/in-memory changes.
                if (hasPlaytime) result.GamesUpdated++;
                result.AchievementsUpdated += achievementCount;
                if (game.SteamAchievementsLastError != null)
                {
                    Warn(game.Id, game.SteamAchievementsLastError);
                    if (game.SteamAchievementsLastError != "setsUnavailable")
                    {
                        result.FailedGames++;
                        result.AchievementsSyncSucceeded = false;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                result.FailedGames++;
                result.AchievementsSyncSucceeded = false;
                Warn(gameId, "gameSyncFailed");
                _logger.LogWarning("Steam sync could not commit game {GameId} ({FailureType}); previous committed games are retained", gameId, ex.GetType().Name);
            }
            finally { _context.ChangeTracker.Clear(); }
            result.ProcessedGames++;
            Report();
        }
        return result;
    }

    public async Task<SteamImportResult> ImportLibraryAsync(int userId, SteamImportRequest request)
    {
        using var syncGate = await AcquireSyncGateAsync(userId);
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

            // Use the horizontal Steam header as the game's hero image.
            var assets = await _steamStore.GetAssetUrlsAsync(appId);
            var heroUrl = IsLegacySteamAssetUrl(importItem.HeroUrl) ? assets?.HeroUrl : importItem.HeroUrl;
            heroUrl ??= assets?.HeroUrl ?? appDetails?.HeaderImageUrl ?? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";
            var coverUrl = IsLegacySteamAssetUrl(importItem.CoverUrl) ? assets?.CoverUrl : importItem.CoverUrl;
            coverUrl ??= assets?.CoverUrl ?? GetSteamLibraryCoverUrl(appId);

            var (criticScore, criticProvider) = await ResolveSteamCriticScoreAsync(appId, appDetails);

            var resolvedLogoUrl = assets?.LogoUrl ?? importItem.LogoUrl ?? ownedGame?.IconUrl;
            if (string.IsNullOrWhiteSpace(resolvedLogoUrl))
                resolvedLogoUrl = await _steamStore.GetCommunityIconUrlAsync(appId);

            var newGame = CreateSteamGameEntity(
                userId,
                defaultStatus.Id,
                gameName,
                appId,
                appDetails,
                criticScore,
                criticProvider,
                steamPlatform?.Id,
                heroUrl,
                coverUrl,
                resolvedLogoUrl);
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

    public async Task<SteamImportedGameDto> AddStoreGameAsync(int userId, int appId, string? logoUrl = null, string? heroUrl = null, string? coverUrl = null)
    {
        using var syncGate = await AcquireSyncGateAsync(userId);
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
        var assets = await _steamStore.GetAssetUrlsAsync(appId);
        var resolvedHeroUrl = IsLegacySteamAssetUrl(heroUrl) ? assets?.HeroUrl : heroUrl;
        resolvedHeroUrl ??= assets?.HeroUrl ?? appDetails?.HeaderImageUrl ?? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";
        var resolvedCoverUrl = IsLegacySteamAssetUrl(coverUrl) ? assets?.CoverUrl : coverUrl;
        resolvedCoverUrl ??= assets?.CoverUrl ?? GetSteamLibraryCoverUrl(appId);

        // Find Steam platform
        var steamPlatform = await _context.GamePlatforms
            .FirstOrDefaultAsync(p => p.UserId == userId && p.Name.ToLower() == "steam");

        // Critic score
        var (criticScore, criticProvider) = await ResolveSteamCriticScoreAsync(appId, appDetails);

        // Prefer the official hashed assets; fall back to the older community/owned sources.
        string? resolvedLogoUrl = assets?.LogoUrl ?? logoUrl;
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
            resolvedLogoUrl = await _steamStore.GetCommunityIconUrlAsync(appId);

        var newGame = CreateSteamGameEntity(
            userId,
            defaultStatus.Id,
            gameName,
            appId,
            appDetails,
            criticScore,
            criticProvider,
            steamPlatform?.Id,
            resolvedHeroUrl,
            resolvedCoverUrl,
            logoUrl: resolvedLogoUrl);

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
        string? heroUrl,
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
            Hero = heroUrl,
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

    private static bool ShouldReplaceWithCommunityIcon(string? logoUrl)
    {
        if (string.IsNullOrWhiteSpace(logoUrl)) return true;
        // Community icons (hash-based jpg) are what we want — keep them.
        // Steam serves these from two CDN path patterns:
        //   Old: .../steamcommunity/public/images/apps/{appId}/{hash}.jpg
        //   New: .../community_assets/images/apps/{appId}/{hash}.jpg  (Akamai, ~2025)
        if (logoUrl.Contains("steamcommunity/public/images/apps/", StringComparison.OrdinalIgnoreCase)) return false;
        if (logoUrl.Contains("community_assets/images/apps/", StringComparison.OrdinalIgnoreCase)) return false;

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
        var shouldResolveHero = string.IsNullOrWhiteSpace(game.Hero);
        var shouldResolveCover = string.IsNullOrWhiteSpace(game.Cover);

        if (!shouldResolveHero && !shouldResolveCover && !shouldResolveLogo) return;

        var appDetails = await _steamStore.GetOrCacheAppDetailsAsync(appId);
        var assets = await _steamStore.GetAssetUrlsAsync(appId);

        if (shouldResolveHero)
            game.Hero = assets?.HeroUrl ?? appDetails?.HeaderImageUrl ?? importItem?.HeroUrl ?? $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/header.jpg";

        if (shouldResolveCover)
            game.Cover = assets?.CoverUrl ?? importItem?.CoverUrl ?? GetSteamLibraryCoverUrl(appId);

        if (shouldResolveLogo)
        {
            if (!string.IsNullOrWhiteSpace(assets?.LogoUrl))
            {
                game.Logo = assets.LogoUrl;
            }
            else if (!string.IsNullOrWhiteSpace(ownedGame?.IconUrl))
            {
                game.Logo = ownedGame.IconUrl;
            }
            else if (!string.IsNullOrWhiteSpace(importItem?.LogoUrl))
            {
                game.Logo = importItem.LogoUrl;
            }
            else
            {
                game.Logo = await _steamStore.GetCommunityIconUrlAsync(appId);
                if (!string.IsNullOrWhiteSpace(game.Logo)) return;

                // All automatic resolution attempts failed (community icon scraping returned nothing
                // and no owned-game hash icon was available).
                // Do NOT fall back to logo.png – that asset is absent for most Steam games and
                // will only produce a permanent broken-image URL.
                // Instead, clear any existing store-asset URL so the game shows no logo rather
                // than a 404, and the next sync will retry the community icon.
                game.Logo = null;
            }
        }
    }

    private async Task<string> ResolveSteamLibraryCoverUrlAsync(int appId)
    {
        return await _steamStore.GetLibraryCoverUrlAsync(appId)
            ?? GetSteamLibraryCoverUrl(appId);
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
            AchievementsUpdated = achCount,
            AchievementsSyncSucceeded = game.SteamAchievementsLastError is null or "setsUnavailable",
            Warnings = game.SteamAchievementsLastError == null ? [] : [game.SteamAchievementsLastError]
        };
    }

    private async Task<int> SyncAchievementsForGameAsync(string steamId, Game game)
    {
        var result = await _achievementSync.SyncAsync(steamId, game);
        return result.Updated;
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

    private static async Task<IDisposable> AcquireSyncGateAsync(int userId, CancellationToken cancellationToken = default)
    {
        var gate = _syncGates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new SyncGateLease(gate);
    }

    private sealed class SyncGateLease(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
