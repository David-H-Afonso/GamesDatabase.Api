using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services.Steam;

public sealed class SteamAchievementSyncService(GamesDbContext context, ISteamApiService steamApi)
{
    public async Task<SteamAchievementSyncResult> SyncAsync(string steamId, Game game)
    {
        if (!game.SteamAppId.HasValue) return new() { Success = true };
        var appId = game.SteamAppId.Value;
        var existing = await context.SteamAchievements
            .Where(achievement => achievement.UserId == game.UserId && achievement.SteamAppId == appId)
            .ToDictionaryAsync(achievement => achievement.ApiName, StringComparer.Ordinal);
        var sets = await context.SteamAchievementSets
            .Where(set => set.UserId == game.UserId && set.SteamAppId == appId)
            .ToDictionaryAsync(set => set.GroupId);

        // Capture the OLD state before any new definitions or locked DLC can arrive.
        // Migration performs the same backfill for existing installations.
        var previouslyRequired = existing.Values.Where(achievement => achievement.IsActive && !achievement.Archived &&
            (!sets.TryGetValue(achievement.GroupId, out var set) || (set.IsActive && set.IsPublic && !set.DeveloperOnly && !set.Archived))).ToList();
        if (previouslyRequired.Count > 0 && previouslyRequired.All(achievement => achievement.Achieved))
            game.SteamCompletionPreserved = true;

        var snapshot = await steamApi.GetAchievementSnapshotAsync(steamId, appId);
        var incompleteSnapshot = !snapshot.NoAchievements && (snapshot.SupportsSets
            ? snapshot.Schema?.Achievements == null || snapshot.Schema.Groups == null || snapshot.Player?.Groups == null
            : snapshot.LegacySchema == null);
        if (!snapshot.Success || incompleteSnapshot)
        {
            // Do not change unlocks, sets, percentage or the last successful sync.
            game.SteamAchievementsLastError = snapshot.Error ?? (incompleteSnapshot ? "invalidResponse" : "steamUnavailable");
            return new() { Error = game.SteamAchievementsLastError };
        }

        var now = DateTime.UtcNow;
        var updated = 0;
        if (snapshot.SupportsSets)
        {
            var definitions = snapshot.Schema?.Achievements ?? [];
            var definitionsByName = definitions.ToDictionary(achievement => achievement.ApiName, StringComparer.Ordinal);
            var groupDefinitions = snapshot.Schema?.Groups ?? [];
            var groupIds = groupDefinitions.Select(set => set.GroupId).ToHashSet();
            var playerAchievements = (snapshot.Player?.Achievements ?? []).ToDictionary(achievement => achievement.InternalKey);
            var playerGroups = (snapshot.Player?.Groups ?? []).ToDictionary(set => set.GroupId);

            foreach (var achievement in existing.Values.Where(achievement => !definitionsByName.ContainsKey(achievement.ApiName)))
            {
                if (achievement.IsActive) updated++;
                achievement.IsActive = false;
            }
            foreach (var set in sets.Values.Where(set => !groupIds.Contains(set.GroupId)))
                set.IsActive = false;

            foreach (var definition in groupDefinitions)
            {
                if (!sets.TryGetValue(definition.GroupId, out var set))
                {
                    set = new() { UserId = game.UserId, SteamAppId = appId, GroupId = definition.GroupId };
                    context.SteamAchievementSets.Add(set);
                    sets.Add(set.GroupId, set);
                }
                playerGroups.TryGetValue(definition.GroupId, out var playerGroup);
                set.GameId = game.Id;
                set.DisplayName = definition.DisplayName;
                set.DlcAppId = definition.DlcAppId > 0 ? definition.DlcAppId : null;
                set.SortOrder = definition.Order;
                set.Archived = definition.Archived;
                set.DeveloperOnly = definition.DeveloperOnly;
                set.IsPublic = definition.IsPublic;
                set.IsActive = true;
                set.TotalAchievements = definition.TotalAchievements;
                set.CompletionAchievements = definition.CompletionAchievements;
                set.IsAchievable = playerGroup?.IsAchievable;
                // Steam completion survives achievements added after the trophy.
                set.IsCompleted |= definition.TotalAchievements > 0 && playerGroup?.IsCompleted == true;
                set.CompletedAt ??= UnixDate(playerGroup?.TimeCompleted);
                set.LastSynced = now;
            }

            foreach (var definition in definitions)
            {
                if (!existing.TryGetValue(definition.ApiName, out var achievement))
                {
                    achievement = new() { UserId = game.UserId, SteamAppId = appId, ApiName = definition.ApiName };
                    context.SteamAchievements.Add(achievement);
                    existing.Add(achievement.ApiName, achievement);
                }
                var set = sets[definition.GroupId];
                playerAchievements.TryGetValue(definition.InternalKey, out var unlock);
                achievement.GameId = game.Id;
                achievement.InternalKey = definition.InternalKey;
                achievement.GroupId = definition.GroupId;
                achievement.DisplayName = definition.DisplayName;
                achievement.Description = definition.Description;
                achievement.Hidden = definition.Hidden;
                achievement.Archived = definition.Archived;
                achievement.IsActive = set.IsPublic && !set.DeveloperOnly;
                achievement.IconUrl = IconUrl(appId, definition.Icon);
                achievement.IconGrayUrl = IconUrl(appId, definition.IconGray);
                achievement.GlobalPercent = definition.GlobalPercent.HasValue ? Math.Clamp(definition.GlobalPercent.Value, 0, 100) : null;
                // A missing entry is a locked achievement only in a valid snapshot
                // of that set. Archived unlock history is never erased by omission.
                if (unlock != null || (playerGroups.ContainsKey(definition.GroupId) && !definition.Archived && !set.Archived))
                {
                    achievement.Achieved = unlock?.Unlocked == true;
                    achievement.UnlockTime = UnixDate(unlock?.UnlockTime);
                }
                achievement.ProgressType = definition.ProgressType;
                achievement.ProgressMin = definition.ProgressType == 2 ? definition.MinProgressFloat : definition.MinProgressInt;
                achievement.ProgressMax = definition.ProgressType == 2 ? definition.MaxProgressFloat : definition.MaxProgressInt;
                achievement.ProgressCurrent = unlock?.ProgressFloat ?? unlock?.ProgressInt ?? (achievement.Achieved ? achievement.ProgressMax : null);
                achievement.LastSynced = now;
                updated++;
            }
            game.SteamAchievementsSource = "sets";
            game.SteamAchievementSchemaVersion = snapshot.Schema?.SchemaVersion;
            game.SteamAchievementSchemaHash = snapshot.Schema?.SchemaHash;
        }
        else
        {
            // The legacy adapter must not flatten/delete previously known sets.
            var player = snapshot.LegacyAchievements.ToDictionary(achievement => achievement.ApiName, StringComparer.Ordinal);
            foreach (var definition in snapshot.LegacySchema?.Achievements ?? [])
            {
                if (!existing.TryGetValue(definition.ApiName, out var achievement))
                {
                    achievement = new() { UserId = game.UserId, SteamAppId = appId, ApiName = definition.ApiName };
                    context.SteamAchievements.Add(achievement);
                    existing.Add(achievement.ApiName, achievement);
                }
                achievement.GameId = game.Id;
                achievement.DisplayName = definition.DisplayName;
                achievement.Description = definition.Description;
                achievement.IconUrl = definition.Icon;
                achievement.IconGrayUrl = definition.IconGray;
                achievement.Hidden = definition.Hidden == 1;
                if (player.TryGetValue(definition.ApiName, out var unlock))
                {
                    achievement.Achieved = unlock.Achieved == 1;
                    achievement.UnlockTime = UnixDate(unlock.UnlockTime);
                }
                achievement.LastSynced = now;
                updated++;
            }
            game.SteamAchievementsSource ??= "legacy";
        }

        var required = existing.Values.Where(achievement => achievement.IsActive && !achievement.Archived &&
            (!sets.TryGetValue(achievement.GroupId, out var set) || (set.IsActive && set.IsPublic && !set.DeveloperOnly && !set.Archived))).ToList();
        game.SteamCompletionPreserved |= sets.Values.Any(set => set.IsActive && set.IsPublic && !set.DeveloperOnly && set.TotalAchievements > 0 && set.IsCompleted) ||
            (required.Count > 0 && required.All(achievement => achievement.Achieved));
        game.SteamAchievementsSyncedAt = now;
        game.SteamAchievementsLastError = snapshot.SupportsSets ? null : "setsUnavailable";
        return new() { Success = true, Updated = updated };
    }

    private static DateTime? UnixDate(long? value) => value is > 0 and <= 253402300799
        ? DateTimeOffset.FromUnixTimeSeconds(value.Value).UtcDateTime : null;

    private static string? IconUrl(int appId, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute))
            return absolute.Scheme == "https" && absolute.Host.EndsWith(".steamstatic.com", StringComparison.OrdinalIgnoreCase) ? value : null;
        if (value.Contains('/') || value.Contains('\\')) return null;
        return $"https://shared.fastly.steamstatic.com/community_assets/images/apps/{appId}/{Uri.EscapeDataString(value)}";
    }
}
