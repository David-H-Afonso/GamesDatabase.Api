using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Application.Services.Steam;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services;

public sealed class CollectionService(GamesDbContext context, IGameService gameService, IPlaylistService playlists, IGameDiscoveryService? discoveryService = null) : ICollectionService
{
    private readonly IGameDiscoveryService discovery = discoveryService ?? new GameDiscoveryService(context, TimeProvider.System);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private Task<CollectionData>? loaded;
    private int? loadedUser;
    private sealed record CollectionData(List<Game> Entities, List<GameDto> Games);

    public async Task<HomeConfigDto> GetHomeAsync(int userId)
    {
        var stored = await context.CollectionPreferences.AsNoTracking().SingleOrDefaultAsync(value => value.UserId == userId);
        HomeConfigDto? config = null;
        if (stored is not null)
        {
            try { config = JsonSerializer.Deserialize<HomeConfigDto>(stored.HomeConfigJson, JsonOptions); }
            catch (JsonException) { /* A newer or invalid stored format is not overwritten by reading. */ }
        }
        if (config?.Version != 1 || config.Rows is null || config.Rows.Count == 0 && stored?.HomeConfigJson == "{}") config = await DefaultHomeAsync(userId);
        config.Revision = stored?.Revision ?? 0;
        return config;
    }

    private async Task<HomeConfigDto> DefaultHomeAsync(int userId)
    {
        var lists = await context.Playlists.AsNoTracking().Where(value => value.UserId == userId).OrderBy(value => value.SortOrder).ThenBy(value => value.Id).ToListAsync();
        var next = lists.FirstOrDefault(value => value.Name.Equals("Want to play", StringComparison.OrdinalIgnoreCase)) ?? lists.FirstOrDefault(value => !value.IsAutomatic && !value.Name.Equals("Focus", StringComparison.OrdinalIgnoreCase));
        var saga = lists.FirstOrDefault(value => value.Name.Equals("Metal Gear", StringComparison.OrdinalIgnoreCase));
        var rows = new List<HomeRowDto> { new() { Id = "home-playing", Source = new() { Kind = "playingNow" }, Presentation = "cards", PreviewCount = 6 } };
        if (next is not null) rows.Add(new() { Id = "home-next", Source = new() { Kind = "playlist", PlaylistId = next.Id }, PreviewCount = 12 });
        rows.Add(new() { Id = "home-recommendations", Source = new() { Kind = "recommendations", Recommendations = new() }, PreviewCount = 6 });
        rows.Add(new() { Id = "home-stats", Source = new() { Kind = "summary" }, Presentation = "cards", PreviewCount = 3 });
        if (saga is not null && saga.Id != next?.Id) rows.Add(new() { Id = "home-saga", Source = new() { Kind = "playlist", PlaylistId = saga.Id }, PreviewCount = 12 });
        return new() { Rows = rows };
    }

    public async Task<CatalogServiceResult<HomeConfigDto>> SaveHomeAsync(HomeConfigDto request, int userId)
    {
        if (request.Version != 1 || request.Revision < 0) return CatalogServiceResult<HomeConfigDto>.BadRequest("Unsupported Home configuration.");
        if (await ValidateRowsAsync(request.Rows, userId, true) is { } error) return CatalogServiceResult<HomeConfigDto>.BadRequest(error);
        var stored = await context.CollectionPreferences.SingleOrDefaultAsync(value => value.UserId == userId);
        if ((stored?.Revision ?? 0) != request.Revision) return CatalogServiceResult<HomeConfigDto>.ConflictResult("Home configuration changed in another client. Reload before saving.");
        stored ??= NewPreferences(userId);
        request.Rows.ForEach(row => row.Title = string.IsNullOrWhiteSpace(row.Title) ? null : row.Title.Trim());
        stored.HomeConfigJson = JsonSerializer.Serialize(new HomeConfigDto { Rows = request.Rows }, JsonOptions);
        stored.Revision++;
        stored.UpdatedAt = DateTime.UtcNow;
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateException) { return CatalogServiceResult<HomeConfigDto>.ConflictResult("Home configuration changed in another client. Reload before saving."); }
        request.Revision = stored.Revision;
        return CatalogServiceResult<HomeConfigDto>.Ok(request);
    }

    private CollectionPreferences NewPreferences(int userId)
    {
        var stored = new CollectionPreferences { UserId = userId };
        context.CollectionPreferences.Add(stored);
        return stored;
    }

    private async Task<string?> ValidateRowsAsync(List<HomeRowDto>? rows, int userId, bool requireOwned)
    {
        if (ValidateRowStructure(rows) is { } validation) return validation;
        foreach (var row in rows!.Where(row => row.Source.Kind == "recommendations"))
            if (await discovery.ValidateFiltersAsync(row.Source.Recommendations, userId) is { } error) return error;
        if (requireOwned)
        {
            var listIds = rows!.Where(row => row.Source.Kind == "playlist").Select(row => row.Source.PlaylistId!.Value).Distinct().ToList();
            var viewIds = rows!.Where(row => row.Source.Kind == "view").Select(row => row.Source.ViewId!.Value).Distinct().ToList();
            if (await context.Playlists.CountAsync(value => value.UserId == userId && listIds.Contains(value.Id)) != listIds.Count || await context.GameViews.CountAsync(value => value.UserId == userId && viewIds.Contains(value.Id)) != viewIds.Count) return "One or more sources are no longer available.";
        }
        return null;
    }

    internal static string? ValidateRowStructure(List<HomeRowDto>? rows)
    {
        if (rows is null || rows.Count > 20 || rows.Any(row => row is null)) return "Home supports at most 20 rows.";
        if (rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != rows.Count) return "Row IDs must be unique.";
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.Id) || !Regex.IsMatch(row.Id, "^[A-Za-z0-9][A-Za-z0-9_-]{0,79}$") || row.Title?.Length > 120 || row.PreviewCount is < 1 or > 24 || row.Presentation is not ("cards" or "covers" or "row")) return "Invalid Home row settings.";
            if (row.Source is null || row.Source.Kind is not ("playingNow" or "playlist" or "view" or "filter" or "summary" or "recommendations")) return "Unsupported Home source.";
            if (row.Source.Kind == "playlist" && row.Source.PlaylistId is not > 0 || row.Source.Kind == "view" && row.Source.ViewId is not > 0) return "A source ID is required.";
            if (row.Source.Kind == "filter" && (row.Source.Rules is null || PlaylistService.ValidateRules(row.Source.Rules) is not null)) return "Invalid row filters.";
            if (row.Source.Kind == "recommendations" && GameDiscoveryService.ValidateFilterStructure(row.Source.Recommendations) is { } error) return error;
        }
        return null;
    }

    public async Task<CatalogServiceResult<List<HomeRowResultDto>>> ResolveHomeAsync(HomeResolveRequest request, int userId)
    {
        if (await ValidateRowsAsync(request.Rows, userId, false) is { } error) return CatalogServiceResult<List<HomeRowResultDto>>.BadRequest(error);
        var results = new List<HomeRowResultDto>();
        foreach (var row in request.Rows.Where(row => row.Enabled))
        {
            var result = new HomeRowResultDto { RowId = row.Id };
            switch (row.Source.Kind)
            {
                case "playlist":
                    var list = await playlists.GetPlaylistByIdAsync(row.Source.PlaylistId!.Value, userId);
                    if (list is null) { result.State = "unavailable"; result.ErrorCode = "sourceUnavailable"; break; }
                    result.SourceName = list.Name;
                    result.TotalMatches = list.Items.Count;
                    result.Items = list.Items.OrderBy(item => item.Position).Take(row.PreviewCount).Select(item => item.Game).ToList();
                    break;
                case "view":
                    var view = await context.GameViews.AsNoTracking().SingleOrDefaultAsync(value => value.UserId == userId && value.Id == row.Source.ViewId);
                    if (view is null) { result.State = "unavailable"; result.ErrorCode = "sourceUnavailable"; break; }
                    var viewed = await gameService.GetGamesAsync(new() { ViewId = view.Id, PageSize = row.PreviewCount }, userId);
                    if (!viewed.Success) { result.State = "error"; result.ErrorCode = "sourceUnavailable"; break; }
                    result.SourceName = view.Name; result.TotalMatches = viewed.Data!.TotalCount; result.Items = viewed.Data.Data.ToList();
                    break;
                case "filter":
                    var preview = await playlists.PreviewAsync(new() { Rules = row.Source.Rules!, PreviewCount = row.PreviewCount }, userId);
                    if (!preview.Success) { result.State = "error"; result.ErrorCode = "invalidFilters"; break; }
                    result.TotalMatches = preview.Data!.ReturnedCount; result.Items = preview.Data.Games;
                    break;
                case "playingNow":
                    var playing = QueryGames(userId).Where(game => game.Status.StatusType == SpecialStatusType.Playing);
                    var focusId = await context.Playlists.Where(value => value.UserId == userId && !value.IsAutomatic && value.Name.ToLower() == "focus").Select(value => (int?)value.Id).FirstOrDefaultAsync();
                    var focus = focusId.HasValue ? await context.PlaylistItems.Where(item => item.PlaylistId == focusId).OrderBy(item => item.Position).Select(item => item.GameId).Take(4).ToListAsync() : [];
                    var f0 = focus.ElementAtOrDefault(0); var f1 = focus.ElementAtOrDefault(1); var f2 = focus.ElementAtOrDefault(2); var f3 = focus.ElementAtOrDefault(3);
                    result.TotalMatches = await playing.CountAsync();
                    var playingGames = await playing.OrderBy(game => game.Id == f0 ? 0 : game.Id == f1 ? 1 : game.Id == f2 ? 2 : game.Id == f3 ? 3 : 4).ThenBy(game => game.Name).Take(row.PreviewCount).ToListAsync();
                    result.Items = playingGames.Select(game => game.ToDto()).ToList();
                    await SteamAchievementProgress.FillGamesAsync(context, result.Items, userId);
                    break;
                case "recommendations":
                    var recommendations = await discovery.DailyAsync(userId, row.Source.Recommendations!, row.PreviewCount);
                    result.TotalMatches = recommendations.TotalMatches;
                    result.Items = recommendations.Items;
                    result.RecommendationDay = recommendations.Day;
                    result.RefreshAt = recommendations.RefreshAt;
                    result.ServerTime = recommendations.ServerTime;
                    break;
                case "summary":
                    result.Summary = (await GetStatisticsAsync(userId, DateTime.UtcNow.Year, null, "finished", false)).Totals;
                    break;
            }
            result.ReturnedCount = result.Items.Count;
            if (result.State == "ready" && row.Source.Kind != "summary" && result.TotalMatches == 0) result.State = "empty";
            results.Add(result);
        }
        return CatalogServiceResult<List<HomeRowResultDto>>.Ok(results);
    }

    public async Task<PagedResult<GameDto>?> GetHomeRowGamesAsync(string rowId, int userId, int page, int pageSize, string? search)
    {
        var row = (await GetHomeAsync(userId)).Rows.FirstOrDefault(value => value.Id == rowId);
        if (row is null) return null;
        page = Math.Clamp(page, 1, 1_000_000); pageSize = Math.Clamp(pageSize, 1, 200);
        var phrase = new NameSearchDto { IncludeTerms = string.IsNullOrWhiteSpace(search) ? [] : [search] };
        if (row.Source.Kind == "filter")
        {
            var result = await playlists.GetRulesPageAsync(row.Source.Rules ?? new(), null, userId, page, pageSize, search);
            return result.Success ? result.Data : null;
        }
        if (row.Source.Kind == "playlist")
        {
            var list = await playlists.GetPlaylistByIdAsync(row.Source.PlaylistId ?? 0, userId);
            if (list is null) return null;
            var games = list.Items.OrderBy(item => item.Position).Select(item => item.Game).Where(game => NameSearchMatching.Matches(game.Name, phrase)).ToList();
            return new() { Data = games.Skip((page - 1) * pageSize).Take(pageSize), TotalCount = games.Count, Page = page, PageSize = pageSize };
        }
        if (row.Source.Kind == "view")
        {
            var result = await gameService.GetGamesAsync(new() { ViewId = row.Source.ViewId, Page = page, PageSize = pageSize }, userId);
            return result.Success ? result.Data : null;
        }
        if (row.Source.Kind == "playingNow")
        {
            var status = await context.GameStatuses.Where(value => value.UserId == userId && value.StatusType == SpecialStatusType.Playing).Select(value => (int?)value.Id).FirstOrDefaultAsync();
            if (!status.HasValue) return new() { Page = page, PageSize = pageSize };
            var result = await gameService.GetGamesAsync(new() { StatusId = status.Value, NameSearch = phrase, SortBy = "status", Page = page, PageSize = pageSize }, userId);
            return result.Success ? result.Data : null;
        }
        if (row.Source.Kind == "recommendations") return await discovery.DailyPageAsync(userId, row.Source.Recommendations!, page, pageSize, search);
        return null;
    }

    public async Task<GotySelectionsDto> GetGotyAsync(int userId)
    {
        var stored = await context.CollectionPreferences.AsNoTracking().SingleOrDefaultAsync(value => value.UserId == userId);
        var selections = stored is null ? [] : JsonSerializer.Deserialize<Dictionary<int, int>>(stored.GotySelectionsJson, JsonOptions) ?? [];
        var owned = await context.Games.Where(game => game.UserId == userId && selections.Values.Contains(game.Id)).Select(game => game.Id).ToListAsync();
        return new() { Revision = stored?.Revision ?? 0, Selections = selections.Where(pair => owned.Contains(pair.Value)).ToDictionary() };
    }

    public async Task<CatalogServiceResult<GotySelectionsDto>> SaveGotyAsync(int year, GotySelectionRequest request, int userId)
    {
        if (year is < 1900 or > 2100 || request.Revision < 0 || request.GameId is <= 0) return CatalogServiceResult<GotySelectionsDto>.BadRequest("Invalid GOTY selection.");
        if (request.GameId.HasValue)
        {
            var yearText = year.ToString(CultureInfo.InvariantCulture);
            var game = await context.Games.AsNoTracking().Include(value => value.GameReplays).SingleOrDefaultAsync(value => value.UserId == userId && value.Id == request.GameId);
            if (game is null) return CatalogServiceResult<GotySelectionsDto>.NotFoundResult("Game unavailable.");
            if (!(new[] { game.Started, game.Finished }.Concat(game.GameReplays.SelectMany(replay => new[] { replay.Started, replay.Finished }))).Any(value => IsoDate(value)?.StartsWith(yearText + "-", StringComparison.Ordinal) == true)) return CatalogServiceResult<GotySelectionsDto>.BadRequest("Choose a game with a stored start or finish date in this year.");
        }
        var stored = await context.CollectionPreferences.SingleOrDefaultAsync(value => value.UserId == userId);
        if ((stored?.Revision ?? 0) != request.Revision) return CatalogServiceResult<GotySelectionsDto>.ConflictResult("Preferences changed. Reload before saving.");
        stored ??= NewPreferences(userId);
        var selections = JsonSerializer.Deserialize<Dictionary<int, int>>(stored.GotySelectionsJson, JsonOptions) ?? [];
        if (request.GameId.HasValue) selections[year] = request.GameId.Value; else selections.Remove(year);
        stored.GotySelectionsJson = JsonSerializer.Serialize(selections, JsonOptions); stored.Revision++; stored.UpdatedAt = DateTime.UtcNow;
        try { await context.SaveChangesAsync(); }
        catch (DbUpdateException) { return CatalogServiceResult<GotySelectionsDto>.ConflictResult("Preferences changed. Reload before saving."); }
        return CatalogServiceResult<GotySelectionsDto>.Ok(new() { Revision = stored.Revision, Selections = selections });
    }

    private IQueryable<Game> QueryGames(int userId) => context.Games.AsNoTracking().AsSplitQuery().Where(game => game.UserId == userId)
        .Include(game => game.Status).Include(game => game.Platform).Include(game => game.PlayedStatus)
        .Include(game => game.GamePlayWiths).ThenInclude(mapping => mapping.PlayWith);

    private Task<CollectionData> LoadAsync(int userId)
    {
        if (loadedUser != userId) { loaded = null; loadedUser = userId; }
        return loaded ??= LoadCoreAsync(userId);
    }

    private async Task<CollectionData> LoadCoreAsync(int userId)
    {
        var entities = await QueryGames(userId).Include(game => game.GameReplays).ThenInclude(replay => replay.ReplayType).ToListAsync();
        var games = entities.Select(game => game.ToDto()).ToList();
        await SteamAchievementProgress.FillGamesAsync(context, games, userId);
        return new(entities, games);
    }

    public static string? IsoDate(string? value)
    {
        if (value is null || value.Length < 10 || value.Length > 10 && value[10] is not ('T' or ' ')) return null;
        return DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ? value[..10] : null;
    }

    private static List<CollectionActivityDto> Activities(CollectionData data, string dateKind)
    {
        var mapped = data.Games.ToDictionary(game => game.Id); var records = new List<CollectionActivityDto>();
        foreach (var game in data.Entities)
        {
            var date = IsoDate(dateKind == "started" ? game.Started : game.Finished);
            if (date is not null) records.Add(new("game-" + game.Id, "game", game.Id, mapped[game.Id], date, game.Grade, "game"));
            foreach (var replay in game.GameReplays)
            {
                date = IsoDate(dateKind == "started" ? replay.Started : replay.Finished);
                if (date is not null) records.Add(new("replay-" + replay.Id, "replay", replay.Id, mapped[game.Id], date, replay.Grade, replay.ReplayType?.Name ?? "Replay"));
            }
        }
        return records.OrderByDescending(record => record.Date, StringComparer.Ordinal).ThenBy(record => record.Game.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<CollectionActivityDto> Best(IEnumerable<CollectionActivityDto> records) => records.GroupBy(record => record.Game.Id)
        .Select(group => group.OrderByDescending(record => record.Grade ?? -1).ThenByDescending(record => record.Date, StringComparer.Ordinal).First())
        .OrderByDescending(record => record.Grade ?? -1).ThenBy(record => record.Game.Name, StringComparer.OrdinalIgnoreCase).ToList();

    private static string Period(int year, int? month) => year.ToString(CultureInfo.InvariantCulture) + (month.HasValue ? $"-{month:00}" : "-");
    private static long? EffectiveMinutes(GameDto game) => GamePlaytime.Effective(game.ManualPlaytimeMinutes, game.SteamPlaytimeForever, game.ManualPlaytimeIsAdditive);

    public async Task<CollectionStatisticsDto> GetStatisticsAsync(int userId, int year, int? month, string dateKind, bool allRanking)
    {
        var data = await LoadAsync(userId); var games = data.Games;
        var allRecords = Activities(data, dateKind);
        var records = allRecords.Where(record => record.Date.StartsWith(Period(year, month), StringComparison.Ordinal)).ToList();
        var best = Best(records); var rated = best.Where(record => record.Grade.HasValue).ToList();
        var unlocks = await SteamAchievementProgress.RequiredAchievements(context, userId).AsNoTracking().Where(value => value.Achieved)
            .Select(value => new { GameId = value.GameId!.Value, value.UnlockTime }).ToListAsync();
        var dated = unlocks.Where(value => value.UnlockTime.HasValue && value.UnlockTime.Value.Year is >= 2000 and <= 2100).ToList();
        var periodUnlocks = dated.Where(value => value.UnlockTime!.Value.Year == year && (!month.HasValue || value.UnlockTime.Value.Month == month)).ToList();
        var tracked = games.Where(game => game.SteamAchievementsTotal > 0).ToList();
        var starts = Activities(data, "started"); var finishes = dateKind == "finished" ? allRecords : Activities(data, "finished");
        var years = starts.Concat(finishes).Select(record => int.Parse(record.Date[..4], CultureInfo.InvariantCulture)).Concat(dated.Select(value => value.UnlockTime!.Value.Year)).Append(DateTime.UtcNow.Year).Distinct().OrderDescending().ToList();
        var mapped = games.ToDictionary(game => game.Id);
        return new()
        {
            Year = year, Month = month, DateKind = dateKind, Years = years,
            Totals = new()
            {
                TotalGames = games.Count, EffectiveMinutes = games.Sum(game => (long)(EffectiveMinutes(game) ?? 0)),
                SteamMinutes = games.Where(game => game.SteamAppId.HasValue).Sum(game => (long)(game.SteamPlaytimeForever ?? 0)),
                ManualMinutes = games.Sum(game => (long)(game.ManualPlaytimeMinutes ?? 0)), TimedGames = games.Count(game => EffectiveMinutes(game).HasValue), MissingTimeGames = games.Count(game => !EffectiveMinutes(game).HasValue),
                CompletedGames = games.Count(game => game.IsCompleted), FullyCompletedGames = games.Count(game => game.IsFullyCompleted), ClosureGames = finishes.Select(record => record.Game.Id).Distinct().Count(), MissingFinishGames = games.Count(game => game.IsCompleted && IsoDate(game.Finished) is null), MissingStoryGames = games.Count(game => !game.Story.HasValue),
                AchievementsUnlocked = tracked.Sum(game => game.SteamAchievementsUnlocked), AchievementsTotal = tracked.Sum(game => game.SteamAchievementsTotal), AchievementGames = tracked.Count, UnknownAchievementDates = unlocks.Count - dated.Count
            },
            ByStatus = data.Entities.GroupBy(game => game.StatusId).Select(group => new CollectionStatusCountDto(group.Key, group.First().Status.Name, group.First().Status.Color, group.Count())).OrderByDescending(value => value.Count).ToList(),
            Months = Enumerable.Range(1, 12).Select(value =>
            {
                var matching = allRecords.Where(record => record.Date.StartsWith(Period(year, value), StringComparison.Ordinal)).ToList();
                return new CollectionMonthDto(value, matching.Count, matching.Select(record => record.Game.Id).Distinct().Count(), matching.Count(record => record.Source == "game"), matching.Count(record => record.Source == "replay"), dated.Count(unlock => unlock.UnlockTime!.Value.Year == year && unlock.UnlockTime.Value.Month == value), Best(matching).FirstOrDefault()?.Game);
            }).ToList(),
            Records = records, DistinctGames = best.Count, MainRecords = records.Count(record => record.Source == "game"), ReplayRecords = records.Count(record => record.Source == "replay"), RatedGames = rated.Count, Average = rated.Count == 0 ? null : rated.Average(record => record.Grade!.Value),
            Ranking = allRanking ? games.Where(game => game.Grade.HasValue).OrderByDescending(game => game.Grade).ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase).Select(game => new CollectionActivityDto("game-" + game.Id, "game", game.Id, game, IsoDate(game.Finished) ?? "", game.Grade, "game")).ToList() : rated,
            DatedUnlocks = periodUnlocks.Count,
            UnlocksByGame = periodUnlocks.GroupBy(value => value.GameId).Where(group => mapped.ContainsKey(group.Key)).Select(group => new CollectionUnlockGameDto(mapped[group.Key], group.Count())).OrderByDescending(value => value.Count).ThenBy(value => value.Game.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            ProgressBins = new[] { 0, 25, 50, 75, 100 }.Select(minimum => new CollectionProgressBinDto(minimum, minimum == 100 ? "100%" : $"{minimum}–{minimum + 24}%", tracked.Count(game => { var percent = game.SteamAchievementsUnlocked * 100d / game.SteamAchievementsTotal; return percent >= minimum && (minimum == 100 ? percent == 100 : percent < minimum + 25); }))).ToList(),
            NearComplete = tracked.Where(game => game.SteamAchievementsUnlocked > 0 && game.SteamAchievementsUnlocked < game.SteamAchievementsTotal).OrderByDescending(game => game.SteamAchievementsUnlocked * 1d / game.SteamAchievementsTotal).ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase).Take(5).ToList(),
            TopPlaytime = games.Where(game => EffectiveMinutes(game).HasValue).OrderByDescending(EffectiveMinutes).ThenBy(game => game.Name, StringComparer.OrdinalIgnoreCase).Take(12).ToList(),
            Developers = games.Where(game => !string.IsNullOrWhiteSpace(game.Developer)).GroupBy(game => game.Developer!.Trim(), StringComparer.OrdinalIgnoreCase).Select(group => new CollectionDeveloperDto(group.Key, group.Count(), group.Count(game => game.Grade.HasValue), group.Any(game => game.Grade.HasValue) ? group.Where(game => game.Grade.HasValue).Average(game => (double)game.Grade!.Value) : null, group.Take(4).ToList())).OrderByDescending(value => value.Count).ThenBy(value => value.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    public async Task<PagedResult<GameDto>> GetMetricGamesAsync(int userId, string metric, int year, int? month, int? value, string? developer, int page, int pageSize, string? search)
    {
        var data = await LoadAsync(userId);
        IEnumerable<GameDto> games = metric switch
        {
            "all" => data.Games,
            "completed" => data.Games.Where(game => game.IsCompleted),
            "strict" => data.Games.Where(game => game.IsFullyCompleted),
            "timed" => data.Games.Where(game => EffectiveMinutes(game).HasValue).OrderByDescending(EffectiveMinutes),
            "missingTime" => data.Games.Where(game => !EffectiveMinutes(game).HasValue),
            "missingFinish" => data.Games.Where(game => game.IsCompleted && IsoDate(game.Finished) is null),
            "missingStory" => data.Games.Where(game => !game.Story.HasValue),
            "status" => data.Games.Where(game => game.StatusId == value),
            "developer" => data.Games.Where(game => string.Equals(game.Developer?.Trim(), developer?.Trim(), StringComparison.OrdinalIgnoreCase)),
            "progress" when value is 0 or 25 or 50 or 75 or 100 => data.Games.Where(game => game.SteamAchievementsTotal > 0 && game.SteamAchievementsUnlocked * 100d / game.SteamAchievementsTotal >= value && (value == 100 ? game.SteamAchievementsUnlocked == game.SteamAchievementsTotal : game.SteamAchievementsUnlocked * 100d / game.SteamAchievementsTotal < value + 25)),
            "closures" => Best(Activities(data, "finished")).Select(record => record.Game),
            "period" or "started" => Best(Activities(data, metric == "started" ? "started" : "finished").Where(record => record.Date.StartsWith(Period(year, month), StringComparison.Ordinal))).Select(record => record.Game),
            "goty" => Best(Activities(data, "started").Concat(Activities(data, "finished")).Where(record => record.Date.StartsWith(Period(year, null), StringComparison.Ordinal))).Select(record => record.Game),
            _ => throw new ArgumentException("Unknown collection metric.")
        };
        var phrase = new NameSearchDto { IncludeTerms = string.IsNullOrWhiteSpace(search) ? [] : [search] };
        var list = games.Where(game => NameSearchMatching.Matches(game.Name, phrase)).ToList(); page = Math.Clamp(page, 1, 1_000_000); pageSize = Math.Clamp(pageSize, 1, 200);
        return new() { Data = list.Skip((page - 1) * pageSize).Take(pageSize).ToList(), TotalCount = list.Count, Page = page, PageSize = pageSize };
    }
}
