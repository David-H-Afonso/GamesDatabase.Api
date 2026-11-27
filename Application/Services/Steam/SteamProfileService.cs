using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Common;

namespace GamesDatabase.Api.Application.Services.Steam;

public class SteamProfileService : ISteamProfileService
{
    private readonly GamesDbContext _context;
    private readonly ISteamApiService _steamApi;
    private readonly ISteamStoreService _steamStore;
    private readonly IMemoryCache _cache;
    private readonly ILogger<SteamProfileService> _logger;

    public SteamProfileService(
        GamesDbContext context,
        ISteamApiService steamApi,
        ISteamStoreService steamStore,
        IMemoryCache cache,
        ILogger<SteamProfileService> logger)
    {
        _context = context;
        _steamApi = steamApi;
        _steamStore = steamStore;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SteamProfileResponse?> GetProfileAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user?.SteamId == null) return null;

        return new SteamProfileResponse
        {
            SteamId = user.SteamId,
            SteamNickname = user.SteamNickname ?? user.SteamId,
            SteamAvatarUrl = user.SteamAvatarUrl,
            SteamLinkedAt = user.SteamLinkedAt ?? DateTime.UtcNow
        };
    }

    public async Task UnlinkSteamAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return;

        user.SteamId = null;
        user.SteamNickname = null;
        user.SteamAvatarUrl = null;
        user.SteamLinkedAt = null;
        await _context.SaveChangesAsync();
    }

    public async Task<SteamProfileResponse?> LinkSteamManuallyAsync(int userId, string rawSteamId)
    {
        var steamId = ExtractSteamId(rawSteamId);
        if (steamId == null) return null;

        var user = await _context.Users.FindAsync(userId);
        if (user == null) return null;

        var existingLink = await _context.Users.FirstOrDefaultAsync(u => u.SteamId == steamId && u.Id != userId);
        if (existingLink != null) return null;

        var profile = await _steamApi.GetPlayerSummaryAsync(steamId);

        user.SteamId = steamId;
        user.SteamNickname = !string.IsNullOrWhiteSpace(profile?.Nickname) ? profile.Nickname : steamId;
        user.SteamAvatarUrl = profile?.AvatarUrl;
        user.SteamLinkedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return new SteamProfileResponse
        {
            SteamId = steamId,
            SteamNickname = user.SteamNickname ?? steamId,
            SteamAvatarUrl = user.SteamAvatarUrl,
            ProfileUrl = profile?.ProfileUrl,
            IsPublic = profile?.IsPublic ?? false,
            SteamLinkedAt = user.SteamLinkedAt ?? DateTime.UtcNow
        };
    }

    public async Task<(bool Success, string? Error, object? Result)> LinkGameAsync(int userId, SteamLinkGameRequest request)
    {
        var game = await _context.Games.FirstOrDefaultAsync(g => g.Id == request.GameId && g.UserId == userId);
        if (game == null) return (false, "Game not found", null);

        game.SteamAppId = request.AppId;

        var user = await _context.Users.FindAsync(userId);
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
        return (true, null, new { gameId = game.Id, appId = request.AppId });
    }

    public async Task<List<SteamLibraryGameDto>?> GetLibraryAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user?.SteamId == null) return null;

        var ownedGames = await GetCachedOwnedGamesAsync(user.SteamId);

        var gdbGames = await _context.Games
            .Where(g => g.UserId == userId && g.SteamAppId != null)
            .Select(g => new { g.SteamAppId, g.Id, g.Name })
            .ToListAsync();

        var gdbByAppId = gdbGames
            .GroupBy(g => g.SteamAppId!.Value)
            .ToDictionary(g => g.Key, g => g.First());

        return ownedGames.Select(g =>
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
    }

    public async Task<List<SteamAchievementDto>> GetAchievementsAsync(int userId, int gameId)
    {
        return (await GetAchievementProgressAsync(userId, gameId))?.Achievements ?? [];
    }

    public async Task<SteamAchievementProgressDto?> GetAchievementProgressAsync(int userId, int gameId)
    {
        var game = await _context.Games.AsNoTracking().FirstOrDefaultAsync(game => game.Id == gameId && game.UserId == userId);
        if (game == null) return null;
        var dto = game.ToDto();
        await SteamAchievementProgress.FillGamesAsync(_context, [dto], userId);
        var sets = await _context.SteamAchievementSets.AsNoTracking()
            .Where(set => set.UserId == userId && set.GameId == gameId && set.SteamAppId == game.SteamAppId &&
                set.IsActive && set.IsPublic && !set.DeveloperOnly)
            .OrderBy(set => set.GroupId == 0 ? 0 : 1).ThenBy(set => set.SortOrder).ThenBy(set => set.GroupId).ToListAsync();
        var rows = await SteamAchievementProgress.VisibleAchievements(_context, userId).AsNoTracking()
            .Where(achievement => achievement.UserId == userId && achievement.GameId == gameId &&
                achievement.SteamAppId == game.SteamAppId && achievement.IsActive)
            .OrderByDescending(achievement => achievement.Achieved).ThenByDescending(achievement => achievement.UnlockTime).ToListAsync();
        var setMap = sets.ToDictionary(set => set.GroupId);
        var required = rows.Where(achievement => !achievement.Archived &&
            (!setMap.TryGetValue(achievement.GroupId, out var set) || !set.Archived)).ToList();
        var eligible = required.Where(achievement => !setMap.TryGetValue(achievement.GroupId, out var set) || set.IsAchievable == true).ToList();
        var visible = rows.Where(achievement => setMap.TryGetValue(achievement.GroupId, out var set) && set.Archived
            ? rows.Any(other => other.GroupId == set.GroupId && other.Achieved)
            : !achievement.Archived || achievement.Achieved).ToList();
        return new SteamAchievementProgressDto
        {
            GameId = gameId,
            Source = game.SteamAchievementsSource ?? (rows.Count > 0 ? "legacy" : "none"),
            LastSynced = game.SteamAchievementsSyncedAt.HasValue ? SteamDateEvidence.Utc(game.SteamAchievementsSyncedAt.Value) : null,
            LastError = SteamSyncClassification.IsNotice(game.SteamAchievementsLastError) ? null : game.SteamAchievementsLastError,
            Notice = SteamSyncClassification.IsNotice(game.SteamAchievementsLastError) ? game.SteamAchievementsLastError : null,
            IsCompleted = dto.IsCompleted,
            IsFullyCompleted = dto.IsFullyCompleted,
            CompletionPreserved = game.SteamCompletionPreserved,
            TotalAchievements = dto.SteamAchievementsTotal,
            UnlockedAchievements = dto.SteamAchievementsUnlocked,
            EligibleAchievementsTotal = eligible.Count,
            EligibleAchievementsUnlocked = eligible.Count(achievement => achievement.Achieved),
            Sets = sets.Where(set => set.TotalAchievements > 0 &&
                (!set.Archived || rows.Any(achievement => achievement.GroupId == set.GroupId && achievement.Achieved)))
                .Select(set => new SteamAchievementSetDto
                {
                    GroupId = set.GroupId, DisplayName = set.DisplayName, DlcAppId = set.DlcAppId,
                    Archived = set.Archived, IsAchievable = set.IsAchievable, IsCompleted = set.IsCompleted,
                    CompletedAt = set.CompletedAt.HasValue && SteamDateEvidence.IsPlausible(set.CompletedAt.Value) ? SteamDateEvidence.Utc(set.CompletedAt.Value) : null,
                    TotalAchievements = set.Archived ? rows.Count(achievement => achievement.GroupId == set.GroupId) : required.Count(achievement => achievement.GroupId == set.GroupId),
                    UnlockedAchievements = (set.Archived ? rows : required).Count(achievement => achievement.GroupId == set.GroupId && achievement.Achieved)
                }).ToList(),
            Achievements = visible.Select(achievement => new SteamAchievementDto
            {
                Id = achievement.Id, SteamAppId = achievement.SteamAppId, ApiName = achievement.ApiName,
                DisplayName = achievement.DisplayName, Description = achievement.Description,
                Achieved = achievement.Achieved,
                UnlockTime = achievement.UnlockTime.HasValue && SteamDateEvidence.IsPlausible(achievement.UnlockTime.Value) ? SteamDateEvidence.Utc(achievement.UnlockTime.Value) : null,
                IconUrl = achievement.IconUrl, IconGrayUrl = achievement.IconGrayUrl, Hidden = achievement.Hidden,
                GroupId = achievement.GroupId, Archived = achievement.Archived || (setMap.TryGetValue(achievement.GroupId, out var set) && set.Archived),
                GlobalPercent = achievement.GlobalPercent, ProgressCurrent = achievement.ProgressCurrent,
                ProgressMin = achievement.ProgressMin, ProgressMax = achievement.ProgressMax
            }).ToList()
        };
    }

    public async Task<List<SteamMatchSuggestionDto>> GetMatchSuggestionsAsync(int userId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user?.SteamId == null) return [];

        var steamGames = await GetCachedOwnedGamesAsync(user.SteamId);

        var gdbGames = await _context.Games
            .Where(g => g.UserId == userId)
            .Select(g => new MatchGameCandidate(g.Id, g.Name, g.SteamAppId))
            .ToListAsync();

        var linkedAppIds = gdbGames
            .Where(g => g.SteamAppId.HasValue)
            .Select(g => g.SteamAppId!.Value)
            .ToHashSet();

        var unlinkedSteam = steamGames.Where(g => !linkedAppIds.Contains(g.AppId)).ToList();
        var unlinkedGdb = gdbGames.Where(g => !g.SteamAppId.HasValue).ToList();
        var dismissedPairs = await GetDismissedPairsAsync(userId);

        return unlinkedSteam
            .Select(sg => FindBestMatchSuggestion(sg.AppId, sg.Name, sg.IconUrl, unlinkedGdb, dismissedPairs))
            .Where(s => s != null)
            .Cast<SteamMatchSuggestionDto>()
            .OrderByDescending(s => s.Confidence)
            .ToList();
    }

    public async Task<List<SteamMatchSuggestionDto>> GetStoreMatchSuggestionsAsync(int userId)
    {
        var gdbGames = await _context.Games
            .Where(g => g.UserId == userId && !g.SteamAppId.HasValue)
            .OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE"))
            .Select(g => new MatchGameCandidate(g.Id, g.Name, null))
            .ToListAsync();

        var dismissedPairs = await GetDismissedPairsAsync(userId);
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
                    storeGame.AppId, storeGame.Name, storeGame.LogoUrl ?? storeGame.CoverUrl,
                    gg.Id, gg.Name, dismissedPairs))
                .Where(s => s != null)
                .Cast<SteamMatchSuggestionDto>()
                .OrderByDescending(s => s.Confidence)
                .FirstOrDefault();

            if (best != null)
                suggestions.Add(best);
        }

        return suggestions.OrderByDescending(s => s.Confidence).ThenBy(s => s.GdbGameName).ToList();
    }

    public async Task<(int Dismissed, string? Error)> DismissMatchSuggestionsAsync(int userId, SteamDismissMatchSuggestionsRequest request)
    {
        var requestedPairs = request.Suggestions
            .Where(s => s.SteamAppId > 0 && s.GdbGameId > 0)
            .GroupBy(s => new { s.SteamAppId, s.GdbGameId })
            .Select(g => g.Key)
            .ToList();

        if (requestedPairs.Count == 0)
            return (0, "No suggestions provided");

        var gameIds = requestedPairs.Select(p => p.GdbGameId).Distinct().ToList();
        var validGameIds = (await _context.Games
            .Where(g => g.UserId == userId && gameIds.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync())
            .ToHashSet();

        var steamAppIds = requestedPairs.Select(p => p.SteamAppId).Distinct().ToList();
        var existingPairs = (await _context.SteamMatchDismissals
            .Where(d => d.UserId == userId && steamAppIds.Contains(d.SteamAppId))
            .Select(d => new { d.SteamAppId, d.GameId })
            .ToListAsync())
            .Select(d => MatchKey(d.SteamAppId, d.GameId))
            .ToHashSet();

        var created = 0;
        foreach (var pair in requestedPairs)
        {
            if (!validGameIds.Contains(pair.GdbGameId)) continue;
            var key = MatchKey(pair.SteamAppId, pair.GdbGameId);
            if (existingPairs.Contains(key)) continue;

            _context.SteamMatchDismissals.Add(new SteamMatchDismissal
            {
                UserId = userId,
                SteamAppId = pair.SteamAppId,
                GameId = pair.GdbGameId,
                CreatedAt = DateTime.UtcNow
            });
            existingPairs.Add(key);
            created++;
        }

        if (created > 0)
            await _context.SaveChangesAsync();

        return (created, null);
    }

    public async Task<List<SteamDateSuggestionDto>?> GetDateSuggestionsAsync(int userId, int? gameId, bool includeStarted, bool includeActivityReferences = false, string timeZone = "UTC")
    {
        var zone = ReleaseDateMatching.FindTimeZone(timeZone) ?? throw new ArgumentException("Invalid date-review time zone.");
        var user = await _context.Users.FindAsync(userId);
        if (user?.SteamId == null) return null;

        var gamesQuery = _context.Games
            .AsNoTracking()
            .Where(g => g.UserId == userId && g.SteamAppId.HasValue);

        if (gameId.HasValue)
            gamesQuery = gamesQuery.Where(g => g.Id == gameId.Value);

        var games = await gamesQuery
            .OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE"))
            .ToListAsync();
        if (games.Count == 0) return [];

        var ownedGames = (await GetCachedOwnedGamesAsync(user.SteamId))
            .GroupBy(game => game.AppId).ToDictionary(group => group.Key, group => group.First());

        var appIds = games.Select(g => g.SteamAppId!.Value).Distinct().ToList();
        var cachedAppDetails = await _context.SteamAppCaches
            .AsNoTracking()
            .Where(a => appIds.Contains(a.AppId))
            .ToDictionaryAsync(a => a.AppId, a => a);

        var unlocks = await _context.SteamAchievements
                .AsNoTracking()
                .Where(a => a.UserId == userId
                    && appIds.Contains(a.SteamAppId)
                    && a.Achieved
                    && a.UnlockTime.HasValue)
                .Select(a => new { AppId = a.SteamAppId, UnlockTime = a.UnlockTime!.Value })
                .ToListAsync();
        // Old cache rows can contain Unix sentinels. Filter before taking Min/Max
        // so a single1970 row does not hide a genuine later first achievement.
        var achievementDates = unlocks.Where(value => SteamDateEvidence.IsPlausible(value.UnlockTime)).GroupBy(value => value.AppId)
            .ToDictionary(group => group.Key, group => (First: SteamDateEvidence.Day(group.Min(value => value.UnlockTime), zone), Last: SteamDateEvidence.Day(group.Max(value => value.UnlockTime), zone)));

        var suggestions = new List<SteamDateSuggestionDto>();

        foreach (var game in games)
        {
            var appId = game.SteamAppId!.Value;
            ownedGames.TryGetValue(appId, out var ownedGame);
            cachedAppDetails.TryGetValue(appId, out var appDetails);

            var notes = new List<string>();
            achievementDates.TryGetValue(appId, out var dates);
            var lastPlayed = SteamDateEvidence.Day(ownedGame?.LastPlayedAt, zone);
            // Only a client that understands activity references may offer the
            // last-achievement fallback. Legacy UI calls it a Steam finish date.
            var fallback = includeActivityReferences ? dates.Last : null;
            var useFallback = fallback != null && (lastPlayed == null || string.CompareOrdinal(fallback, lastPlayed) > 0);
            var proposedFinished = useFallback ? fallback : lastPlayed;
            var finishedSource = useFallback ? "lastAchievement" : lastPlayed != null ? "lastPlayed" : "none";
            if (lastPlayed == null) notes.Add("noLastPlayed");
            if (useFallback) notes.Add("lastAchievementFallback");

            var hasFinished = !string.IsNullOrWhiteSpace(game.Finished);
            var isRejectedFinished = proposedFinished != null && game.SteamFinishedRejectedValue == proposedFinished;
            var shouldSuggestFinished = proposedFinished != null && !isRejectedFinished && !hasFinished;

            string? proposedStarted = null;
            var startedSource = "none";
            if (includeStarted && string.IsNullOrWhiteSpace(game.Started))
            {
                if (dates.First is { } firstUnlockValue)
                {
                    if (game.SteamStartedRejectedValue != firstUnlockValue)
                    {
                        proposedStarted = firstUnlockValue;
                        startedSource = "firstAchievement";
                    }
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

            // A reference from an earlier playthrough must not contradict a
            // date the owner has already recorded for this playthrough.
            if (proposedStarted != null && DateOnly.TryParseExact(game.Finished, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var savedFinish)
                && DateOnly.ParseExact(proposedStarted, "yyyy-MM-dd", CultureInfo.InvariantCulture) > savedFinish) { proposedStarted = null; startedSource = "none"; }
            if (shouldSuggestFinished && DateOnly.TryParseExact(game.Started, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var savedStart)
                && DateOnly.ParseExact(proposedFinished!, "yyyy-MM-dd", CultureInfo.InvariantCulture) < savedStart) shouldSuggestFinished = false;

            if (proposedStarted == null && !shouldSuggestFinished) continue;

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
                UpdatedAt = SteamDateEvidence.Utc(game.UpdatedAt),
                ReviewTimeZone = timeZone,
                ObservedLastPlayed = lastPlayed,
                FirstAchievementDate = dates.First,
                LastAchievementDate = dates.Last,
                StartedSource = startedSource,
                FinishedSource = finishedSource,
                IsFinishedConflict = false,
                IsFinishedSteamManaged = false,
                Notes = notes
            });
        }

        return suggestions;
    }

    public async Task<SteamApplyDateSuggestionsResponse?> ApplyDateSuggestionsAsync(int userId, SteamApplyDateSuggestionsRequest request)
    {
        if (request?.Suggestions is not { Count: > 0 and <= 1000 } || request.Suggestions.Any(item => item == null || item.GameId <= 0)
            || request.Suggestions.Select(item => item.GameId).Distinct().Count() != request.Suggestions.Count) return null;
        var zone = ReleaseDateMatching.FindTimeZone(request.TimeZone);
        if (zone == null) return null;
        var response = new SteamApplyDateSuggestionsResponse();
        var requested = request.Suggestions
            .Where(s => s.GameId > 0 && (!string.IsNullOrWhiteSpace(s.Started) || !string.IsNullOrWhiteSpace(s.Finished)))
            .GroupBy(s => s.GameId)
            .Select(g => g.First())
            .ToList();

        if (requested.Count == 0) return null;

        await using var transaction = await _context.Database.BeginTransactionAsync();
        var gameIds = requested.Select(s => s.GameId).ToList();
        var games = await _context.Games
            .Where(g => g.UserId == userId && gameIds.Contains(g.Id) && g.SteamAppId.HasValue)
            .ToDictionaryAsync(g => g.Id, g => g);

        var applied = new List<(Game Game, string? Started, string? Finished)>();
        void Skip(int id, string reason) { response.Skipped.Add(new(id, reason)); response.Errors.Add($"{reason}:{id}"); }

        foreach (var suggestion in requested)
        {
            if (!games.TryGetValue(suggestion.GameId, out var game))
            {
                Skip(suggestion.GameId, "unavailable");
                continue;
            }
            if (suggestion.ExpectedUpdatedAt.HasValue && SteamDateEvidence.Utc(suggestion.ExpectedUpdatedAt.Value) != SteamDateEvidence.Utc(game.UpdatedAt))
            {
                Skip(game.Id, "changed");
                continue;
            }
            var wantsStarted = !string.IsNullOrWhiteSpace(suggestion.Started);
            var wantsFinished = !string.IsNullOrWhiteSpace(suggestion.Finished);
            if (wantsStarted && !SteamDateEvidence.TryDay(suggestion.Started, out _, zone) || wantsFinished && !SteamDateEvidence.TryDay(suggestion.Finished, out _, zone))
            {
                Skip(game.Id, "invalidDate");
                continue;
            }
            var started = wantsStarted && string.IsNullOrWhiteSpace(game.Started) ? suggestion.Started : null;
            var finished = wantsFinished && string.IsNullOrWhiteSpace(game.Finished) ? suggestion.Finished : null;
            if (started == null && finished == null) { Skip(game.Id, "hasDate"); continue; }
            if (DateOnly.TryParseExact(started ?? game.Started, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDay)
                && DateOnly.TryParseExact(finished ?? game.Finished, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var finishDay) && startDay > finishDay)
            {
                Skip(game.Id, "invalidDate");
                continue;
            }
            if (started != null)
            {
                game.Started = started;
                if (game.SteamStartedRejectedValue == started) game.SteamStartedRejectedValue = null;
                _context.GameHistoryEntries.Add(new() { UserId = userId, GameId = game.Id, GameName = game.Name, Field = "Started", NewValue = started, ActionType = "Updated", Description = $"Steam activity date explicitly reviewed ({request.TimeZone})", ChangedAt = DateTime.UtcNow });
            }
            if (finished != null)
            {
                game.Finished = finished;
                // Reviewed reference, never a promise to follow every later play.
                game.SteamFinishedSource = "steam-reviewed";
                game.SteamFinishedLastValue = finished;
                game.SteamFinishedSyncedAt = DateTime.UtcNow;
                if (game.SteamFinishedRejectedValue == finished) game.SteamFinishedRejectedValue = null;
                _context.GameHistoryEntries.Add(new() { UserId = userId, GameId = game.Id, GameName = game.Name, Field = "Finished", NewValue = finished, ActionType = "Updated", Description = $"Steam activity date explicitly reviewed ({request.TimeZone})", ChangedAt = DateTime.UtcNow });
            }
            applied.Add((game, started, finished));
        }

        if (applied.Count > 0)
            await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        response.Updated = applied.Count;
        response.Applied = applied.Select(item => new SteamAppliedDateDto(item.Game.Id, item.Started, item.Finished, SteamDateEvidence.Utc(item.Game.UpdatedAt))).ToList();

        return response;
    }

    public async Task<(int Dismissed, string? Error)> DismissDateSuggestionsAsync(int userId, SteamDismissDateSuggestionsRequest request)
    {
        if (request?.Suggestions is not { Count: > 0 and <= 1000 } || request.Suggestions.Any(item => item == null)) return (0, "Invalid date review.");
        var zone = ReleaseDateMatching.FindTimeZone(request.TimeZone);
        if (zone == null) return (0, "Invalid date-review time zone.");
        var requested = request.Suggestions
            .Where(s => s.GameId > 0 && (IsValidDateValue(s.Started, zone) || IsValidDateValue(s.Finished, zone)))
            .GroupBy(s => s.GameId)
            .Select(g => g.First())
            .ToList();

        if (requested.Count == 0)
            return (0, "No date suggestions provided");

        var gameIds = requested.Select(s => s.GameId).ToList();
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var games = await _context.Games
            .Where(g => g.UserId == userId && gameIds.Contains(g.Id) && g.SteamAppId.HasValue)
            .ToDictionaryAsync(g => g.Id, g => g);

        var dismissed = 0;
        foreach (var suggestion in requested)
        {
            if (!games.TryGetValue(suggestion.GameId, out var game)) continue;
            if (suggestion.ExpectedUpdatedAt.HasValue && SteamDateEvidence.Utc(suggestion.ExpectedUpdatedAt.Value) != SteamDateEvidence.Utc(game.UpdatedAt)) continue;

            var changed = false;
            if (IsValidDateValue(suggestion.Started, zone) && game.SteamStartedRejectedValue != suggestion.Started)
            {
                game.SteamStartedRejectedValue = suggestion.Started;
                changed = true;
            }
            if (IsValidDateValue(suggestion.Finished, zone) && game.SteamFinishedRejectedValue != suggestion.Finished)
            {
                game.SteamFinishedRejectedValue = suggestion.Finished;
                changed = true;
            }
            if (changed) dismissed++;
        }

        if (dismissed > 0)
            await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return (dismissed, null);
    }

    public async Task<(bool Created, string? SteamId, string? SteamNickname, string? SteamAvatarUrl)> LinkSteamAccountAsync(int userId, string steamId)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null) return (false, null, null, null);

        var existingLink = await _context.Users.FirstOrDefaultAsync(u => u.SteamId == steamId && u.Id != userId);
        if (existingLink != null) return (false, null, null, null);

        var profile = await _steamApi.GetPlayerSummaryAsync(steamId);

        user.SteamId = steamId;
        user.SteamNickname = profile?.Nickname;
        user.SteamAvatarUrl = profile?.AvatarUrl;
        user.SteamLinkedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        return (true, steamId, profile?.Nickname, profile?.AvatarUrl);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private sealed record MatchGameCandidate(int Id, string Name, int? SteamAppId);

    private async Task<HashSet<string>> GetDismissedPairsAsync(int userId) =>
        (await _context.SteamMatchDismissals
            .Where(d => d.UserId == userId)
            .Select(d => new { d.SteamAppId, d.GameId })
            .ToListAsync())
        .Select(d => MatchKey(d.SteamAppId, d.GameId))
        .ToHashSet();

    private static string MatchKey(int steamAppId, int gameId) => $"{steamAppId}:{gameId}";

    private static SteamMatchSuggestionDto? FindBestMatchSuggestion(
        int steamAppId, string steamName, string? steamIconUrl,
        IEnumerable<MatchGameCandidate> gdbGames, HashSet<string> dismissedPairs)
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
        int steamAppId, string steamName, string? steamIconUrl,
        int gdbGameId, string gdbGameName, HashSet<string> dismissedPairs)
    {
        if (dismissedPairs.Contains(MatchKey(steamAppId, gdbGameId))) return null;
        var score = NameScore(steamName, gdbGameName);
        if (score < 50) return null;

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

    internal static string? ExtractSteamId(string input)
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

        if (wa.IsSubsetOf(wb) || wb.IsSubsetOf(wa))
            return Math.Max(80, (int)(100.0 * intersection / Math.Max(wa.Count, wb.Count)));

        int union = wa.Union(wb).Count();
        return (int)(100.0 * intersection / union);
    }

    private static string NormalizeName(string name)
    {
        var normalized = name.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var chars = normalized
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray();
        return Regex.Replace(new string(chars), @"\s+", " ").Trim();
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

    private static bool IsValidDateValue(string? value, TimeZoneInfo? zone = null)
    {
        return SteamDateEvidence.TryDay(value, out _, zone);
    }
}
