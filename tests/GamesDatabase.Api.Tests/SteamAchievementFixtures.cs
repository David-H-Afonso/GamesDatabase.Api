using GamesDatabase.Api.Contracts.Steam;

namespace GamesDatabase.Api.Tests;

internal static class SteamAchievementFixtures
{
    public static SteamAchievementSnapshot Snapshot(int[] sizes, int[] unlocked, params int[] completedGroups)
    {
        var schema = new SteamSetSchema { SchemaVersion = 1, SchemaHash = 100, Achievements = [], Groups = [] };
        var player = new SteamSetPlayerProgress { SchemaVersion = 1, SchemaHash = 100, Achievements = [], Groups = [] };
        long key = 0;
        for (var group = 0; group < sizes.Length; group++)
        {
            schema.Groups.Add(new() { GroupId = group, DisplayName = group == 0 ? "Base" : $"DLC {group}", DlcAppId = group == 0 ? 0 : 2000 + group, IsPublic = true, TotalAchievements = sizes[group], CompletionAchievements = sizes[group] });
            player.Groups.Add(new() { GroupId = group, IsAchievable = true, IsCompleted = completedGroups.Contains(group), TimeCompleted = completedGroups.Contains(group) ? 1700000000 : 0 });
            for (var index = 0; index < sizes[group]; index++, key++)
            {
                schema.Achievements.Add(new() { InternalKey = key, ApiName = $"ACH_{key}", DisplayName = $"Achievement {key}", Description = "Description", GroupId = group, Icon = "achieved.jpg", IconGray = "locked.jpg", GlobalPercent = 7.5 });
                if (index < unlocked[group]) player.Achievements.Add(new() { InternalKey = key, Unlocked = true, UnlockTime = 1700000000 + key });
            }
        }
        return new() { Success = true, SupportsSets = true, Schema = schema, Player = player };
    }
}
