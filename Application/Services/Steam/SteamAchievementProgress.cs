using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services.Steam;

/// <summary>One completion policy for games, playlists and the detailed Steam view.</summary>
public static class SteamAchievementProgress
{
    public static IQueryable<SteamAchievement> VisibleAchievements(GamesDbContext context, int userId) =>
        context.SteamAchievements.Where(achievement => achievement.UserId == userId && achievement.GameId.HasValue &&
            achievement.IsActive &&
            (!achievement.Game!.SteamAppId.HasValue || achievement.Game.SteamAppId == achievement.SteamAppId) &&
            !context.SteamAchievementSets.Any(set => set.UserId == userId && set.SteamAppId == achievement.SteamAppId &&
                set.GroupId == achievement.GroupId && (!set.IsActive || !set.IsPublic || set.DeveloperOnly)));

    public static IQueryable<SteamAchievement> RequiredAchievements(GamesDbContext context, int userId) =>
        VisibleAchievements(context, userId).Where(achievement => !achievement.Archived &&
            !context.SteamAchievementSets.Any(set => set.UserId == userId && set.SteamAppId == achievement.SteamAppId && set.GroupId == achievement.GroupId && set.Archived));

    public static IQueryable<Game> ApplyFilters(IQueryable<Game> query, GamesDbContext context, int userId, bool? completed, bool? allAchievements)
    {
        query = query.Where(game => game.UserId == userId);
        var fullIds = RequiredAchievements(context, userId).GroupBy(achievement => achievement.GameId!.Value)
            .Where(group => group.All(achievement => achievement.Achieved)).Select(group => group.Key);
        var setIds = context.SteamAchievementSets.Where(set => set.UserId == userId && set.GameId.HasValue &&
            set.Game!.SteamAppId == set.SteamAppId && set.IsActive && set.IsPublic && !set.DeveloperOnly && set.TotalAchievements > 0 && set.IsCompleted)
            .Select(set => set.GameId!.Value);

        if (completed.HasValue)
        {
            query = completed.Value
                ? query.Where(game => game.IsManuallyCompleted || game.SteamCompletionPreserved || fullIds.Contains(game.Id) || setIds.Contains(game.Id))
                : query.Where(game => !game.IsManuallyCompleted && !game.SteamCompletionPreserved && !fullIds.Contains(game.Id) && !setIds.Contains(game.Id));
        }
        if (allAchievements.HasValue)
        {
            // Strict means ALL currently available published achievements, including
            // DLC the player cannot access. Archived/unobtainable content is excluded.
            query = allAchievements.Value
                ? query.Where(game => (game.IsManuallyCompleted && !game.SteamAppId.HasValue) || (game.SteamAppId.HasValue && fullIds.Contains(game.Id)))
                : query.Where(game => !(game.IsManuallyCompleted && !game.SteamAppId.HasValue) && !(game.SteamAppId.HasValue && fullIds.Contains(game.Id)));
        }
        return query;
    }

    public static async Task FillGamesAsync(GamesDbContext context, IEnumerable<GameDto> games, int userId)
    {
        var list = games.ToList();
        if (list.Count == 0) return;
        var ids = list.Select(game => game.Id).ToList();
        var stats = await RequiredAchievements(context, userId).AsNoTracking()
            .Where(achievement => ids.Contains(achievement.GameId!.Value))
            .GroupBy(achievement => achievement.GameId!.Value)
            .Select(group => new { GameId = group.Key, Total = group.Count(), Unlocked = group.Count(achievement => achievement.Achieved) })
            .ToDictionaryAsync(group => group.GameId);
        var sets = await context.SteamAchievementSets.AsNoTracking()
            .Where(set => set.UserId == userId && set.GameId.HasValue && ids.Contains(set.GameId.Value) &&
                set.Game!.SteamAppId == set.SteamAppId && set.IsActive && set.IsPublic && !set.DeveloperOnly &&
                set.TotalAchievements > 0 && (!set.Archived || set.IsCompleted))
            .GroupBy(set => set.GameId!.Value)
            .Select(group => new { GameId = group.Key, Total = group.Count(), Completed = group.Count(set => set.IsCompleted) })
            .ToDictionaryAsync(group => group.GameId);

        foreach (var game in list)
        {
            game.SteamAchievementsTotal = 0;
            game.SteamAchievementsUnlocked = 0;
            game.SteamSetsTotal = 0;
            game.SteamSetsCompleted = 0;
            if (stats.TryGetValue(game.Id, out var stat))
            {
                game.SteamAchievementsTotal = stat.Total;
                game.SteamAchievementsUnlocked = stat.Unlocked;
            }
            if (sets.TryGetValue(game.Id, out var setStats))
            {
                game.SteamSetsTotal = setStats.Total;
                game.SteamSetsCompleted = setStats.Completed;
            }
            game.IsFullyCompleted = (game.IsManuallyCompleted && !game.SteamAppId.HasValue) ||
                (game.SteamAppId.HasValue && game.SteamAchievementsTotal > 0 && game.SteamAchievementsUnlocked == game.SteamAchievementsTotal);
            game.IsCompleted = game.IsManuallyCompleted || game.SteamCompletionPreserved || game.IsFullyCompleted ||
                game.SteamSetsCompleted > 0 || (game.SteamAchievementsTotal > 0 && game.SteamAchievementsUnlocked == game.SteamAchievementsTotal);
        }
    }
}
