using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Application.Services.Steam;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services;

/// <summary>Owned discovery reads. One UTC boundary for everyone, no timer or persisted shuffle.</summary>
public sealed class GameDiscoveryService(GamesDbContext context, TimeProvider clock) : IGameDiscoveryService
{
    private IQueryable<Game> Games(int userId) => context.Games.AsNoTracking().AsSplitQuery()
        .Where(game => game.UserId == userId)
        .Include(game => game.Status).Include(game => game.Platform).Include(game => game.PlayedStatus)
        .Include(game => game.GamePlayWiths).ThenInclude(item => item.PlayWith);

    public async Task<GameDto?> RandomAsync(int userId, CancellationToken cancellationToken = default)
    {
        // Sampling an index is uniform over the owned collection, not just its current page.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var count = await context.Games.CountAsync(game => game.UserId == userId, cancellationToken);
            if (count == 0) return null;
            var game = await Games(userId).OrderBy(game => game.Id).Skip(RandomNumberGenerator.GetInt32(count)).FirstOrDefaultAsync(cancellationToken);
            if (game is null) continue; // A concurrent deletion is a read race, not another user's fallback.
            var dto = game.ToDto();
            await SteamAchievementProgress.FillGamesAsync(context, [dto], userId);
            return dto;
        }
        return null;
    }

    public async Task<CatalogServiceResult<GameDto>> RandomDisplayedAsync(int userId, List<int>? gameIds, CancellationToken cancellationToken = default)
    {
        if (gameIds is null || gameIds.Count > 1000 || gameIds.Any(id => id <= 0) || gameIds.Distinct().Count() != gameIds.Count) return CatalogServiceResult<GameDto>.BadRequest("Invalid displayed game scope.");
        if (gameIds.Count == 0) return CatalogServiceResult<GameDto>.NotFoundResult("emptySelection");
        var owned = await context.Games.Where(game => game.UserId == userId && gameIds.Contains(game.Id)).Select(game => game.Id).ToListAsync(cancellationToken);
        // A stale or foreign ID must not broaden the scope or silently choose from a subset.
        if (owned.Count != gameIds.Count) return CatalogServiceResult<GameDto>.ConflictResult("displayedScopeChanged");
        var selected = owned[RandomNumberGenerator.GetInt32(owned.Count)];
        var game = await Games(userId).SingleOrDefaultAsync(game => game.Id == selected, cancellationToken);
        if (game is null) return CatalogServiceResult<GameDto>.ConflictResult("displayedScopeChanged");
        var dto = game.ToDto(); await SteamAchievementProgress.FillGamesAsync(context, [dto], userId);
        return CatalogServiceResult<GameDto>.Ok(dto);
    }

    public static string? ValidateFilterStructure(RecommendationFiltersDto? filters)
    {
        if (filters is null || filters.IncludedStatusIds is null || filters.ExcludedStatusIds is null) return "Recommendation filters are required.";
        if (filters.IncludedStatusIds.Count > 100 || filters.ExcludedStatusIds.Count > 100 || filters.IncludedStatusIds.Concat(filters.ExcludedStatusIds).Any(id => id <= 0)) return "Invalid recommendation statuses.";
        return null;
    }

    public async Task<string?> ValidateFiltersAsync(RecommendationFiltersDto? filters, int userId, CancellationToken cancellationToken = default)
    {
        if (ValidateFilterStructure(filters) is { } error) return error;
        var ids = filters!.IncludedStatusIds.Concat(filters.ExcludedStatusIds).Distinct().ToList();
        return await context.GameStatuses.CountAsync(status => status.UserId == userId && ids.Contains(status.Id), cancellationToken) == ids.Count
            ? null : "A recommendation status is unavailable.";
    }

    private IQueryable<Game> Eligible(int userId, RecommendationFiltersDto filters)
    {
        var query = Games(userId);
        query = filters.IncludedStatusIds.Count == 0
            ? query.Where(game => game.Status.StatusType == SpecialStatusType.None)
            : query.Where(game => filters.IncludedStatusIds.Contains(game.StatusId));
        return query.Where(game => !filters.ExcludedStatusIds.Contains(game.StatusId));
    }

    internal static List<int> OrderForDay(IEnumerable<int> ids, int userId, DateOnly day)
    {
        // A stable user-specific shuffle rotates one position per server day. A
        // small pool may repeat games, but a pool with >=2 games changes order.
        var ordered = ids.OrderBy(id => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{userId}:{id}"))), StringComparer.Ordinal).ToList();
        if (ordered.Count < 2) return ordered;
        var offset = day.DayNumber % ordered.Count;
        return ordered.Skip(offset).Concat(ordered.Take(offset)).ToList();
    }

    private async Task<List<int>> DailyIdsAsync(int userId, RecommendationFiltersDto filters, DateOnly day, string? search, CancellationToken cancellationToken)
    {
        if (await ValidateFiltersAsync(filters, userId, cancellationToken) is { } error) throw new ArgumentException(error);
        var ids = await Eligible(userId, filters).Select(game => game.Id).ToListAsync(cancellationToken);
        var ordered = OrderForDay(ids, userId, day);
        if (string.IsNullOrWhiteSpace(search)) return ordered;
        var phrase = new NameSearchDto { IncludeTerms = [search] };
        var matching = await Eligible(userId, filters).Select(game => new { game.Id, game.Name }).ToListAsync(cancellationToken);
        var found = matching.Where(game => NameSearchMatching.Matches(game.Name, phrase)).Select(game => game.Id).ToHashSet();
        return ordered.Where(found.Contains).ToList();
    }

    private async Task<List<GameDto>> ReadAsync(List<int> ids, int userId, CancellationToken cancellationToken)
    {
        var records = await Games(userId).Where(game => ids.Contains(game.Id)).ToListAsync(cancellationToken);
        var byId = records.ToDictionary(game => game.Id, game => game.ToDto());
        var games = ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
        await SteamAchievementProgress.FillGamesAsync(context, games, userId);
        return games;
    }

    public async Task<DailyRecommendationsDto> DailyAsync(int userId, RecommendationFiltersDto filters, int count, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow();
        var day = DateOnly.FromDateTime(now.UtcDateTime);
        var ids = await DailyIdsAsync(userId, filters, day, null, cancellationToken);
        return new()
        {
            Day = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            RefreshAt = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)),
            ServerTime = now,
            TotalMatches = ids.Count,
            Items = await ReadAsync(ids.Take(Math.Clamp(count, 1, 24)).ToList(), userId, cancellationToken)
        };
    }

    public async Task<PagedResult<GameDto>> DailyPageAsync(int userId, RecommendationFiltersDto filters, int page, int pageSize, string? search, CancellationToken cancellationToken = default)
    {
        page = Math.Clamp(page, 1, 1_000_000); pageSize = Math.Clamp(pageSize, 1, 200);
        var ids = await DailyIdsAsync(userId, filters, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), search, cancellationToken);
        return new() { Page = page, PageSize = pageSize, TotalCount = ids.Count, Data = await ReadAsync(ids.Skip((page - 1) * pageSize).Take(pageSize).ToList(), userId, cancellationToken) };
    }
}
