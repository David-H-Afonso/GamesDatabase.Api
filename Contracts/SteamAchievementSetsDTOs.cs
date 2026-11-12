using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Contracts.Steam;

// IPlayerService methods are isolated here because their public documentation lags
// behind the client protocol. Never treat a missing payload as an empty snapshot.
public sealed class SteamServiceEnvelope<T>
{
    [JsonPropertyName("response")]
    public T? Response { get; set; }
}

public sealed class SteamSetSchema
{
    [JsonPropertyName("achievements")]
    public List<SteamSetAchievementDefinition>? Achievements { get; set; }
    [JsonPropertyName("groups")]
    public List<SteamSetDefinition>? Groups { get; set; }
    [JsonPropertyName("schema_version")]
    public int? SchemaVersion { get; set; }
    [JsonPropertyName("schema_hash")]
    public long? SchemaHash { get; set; }
}

public sealed class SteamSetAchievementDefinition
{
    [JsonPropertyName("internal_name")]
    public string ApiName { get; set; } = string.Empty;
    [JsonRequired, JsonPropertyName("internal_key")]
    public long InternalKey { get; set; }
    [JsonPropertyName("localized_name")]
    public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("localized_desc")]
    public string? Description { get; set; }
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
    [JsonPropertyName("icon_gray")]
    public string? IconGray { get; set; }
    [JsonPropertyName("hidden")]
    public bool Hidden { get; set; }
    [JsonRequired, JsonPropertyName("groupid")]
    public int GroupId { get; set; }
    [JsonRequired, JsonPropertyName("archived")]
    public bool Archived { get; set; }
    [JsonPropertyName("player_percent_unlocked")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public double? GlobalPercent { get; set; }
    [JsonPropertyName("progress_type")]
    public int ProgressType { get; set; }
    [JsonPropertyName("min_progress_int")]
    public double? MinProgressInt { get; set; }
    [JsonPropertyName("max_progress_int")]
    public double? MaxProgressInt { get; set; }
    [JsonPropertyName("min_progress_float")]
    public double? MinProgressFloat { get; set; }
    [JsonPropertyName("max_progress_float")]
    public double? MaxProgressFloat { get; set; }
}

public sealed class SteamSetDefinition
{
    [JsonRequired, JsonPropertyName("groupid")]
    public int GroupId { get; set; }
    [JsonPropertyName("localized_name")]
    public string DisplayName { get; set; } = string.Empty;
    [JsonPropertyName("dlcappid")]
    public int DlcAppId { get; set; }
    [JsonPropertyName("archived")]
    public bool Archived { get; set; }
    [JsonPropertyName("developeronly")]
    public bool DeveloperOnly { get; set; }
    [JsonRequired, JsonPropertyName("ispublic")]
    public bool IsPublic { get; set; }
    [JsonPropertyName("order")]
    public int Order { get; set; }
    [JsonRequired, JsonPropertyName("total_achievements")]
    public int TotalAchievements { get; set; }
    [JsonRequired, JsonPropertyName("completion_achievements")]
    public int CompletionAchievements { get; set; }
}

public sealed class SteamSetPlayerProgress
{
    [JsonPropertyName("achievements")]
    public List<SteamSetAchievementUnlock>? Achievements { get; set; }
    [JsonPropertyName("groups")]
    public List<SteamSetPlayerGroup>? Groups { get; set; }
    [JsonPropertyName("schema_version")]
    public int? SchemaVersion { get; set; }
    [JsonPropertyName("schema_hash")]
    public long? SchemaHash { get; set; }
}

public sealed class SteamSetAchievementUnlock
{
    [JsonRequired, JsonPropertyName("internal_key")]
    public long InternalKey { get; set; }
    // Steam omits unlocked on progress-only records. Missing is not a parse error,
    // but the adapter still requires either this flag or a numeric progress value.
    [JsonPropertyName("unlocked")]
    public bool? Unlocked { get; set; }
    [JsonPropertyName("unlock_time")]
    public long UnlockTime { get; set; }
    [JsonPropertyName("progress_int")]
    public double? ProgressInt { get; set; }
    [JsonPropertyName("progress_float")]
    public double? ProgressFloat { get; set; }
}

public sealed class SteamSetPlayerGroup
{
    [JsonRequired, JsonPropertyName("groupid")]
    public int GroupId { get; set; }
    [JsonRequired, JsonPropertyName("is_achievable")]
    public bool IsAchievable { get; set; }
    [JsonRequired, JsonPropertyName("is_completed")]
    public bool IsCompleted { get; set; }
    [JsonPropertyName("time_completed")]
    public long TimeCompleted { get; set; }
}

public sealed class SteamAchievementSnapshot
{
    public bool Success { get; set; }
    public bool SupportsSets { get; set; }
    public bool NoAchievements { get; set; }
    public string? SkipReason { get; set; }
    public string? Error { get; set; }
    public SteamSetSchema? Schema { get; set; }
    public SteamSetPlayerProgress? Player { get; set; }
    public SteamGameSchemaDto? LegacySchema { get; set; }
    public List<SteamAchievementRaw> LegacyAchievements { get; set; } = [];
}

public sealed class SteamAchievementSyncResult
{
    public bool Success { get; set; }
    public int Updated { get; set; }
    public string? Error { get; set; }
    public string Outcome { get; set; } = "updated";
}

public sealed class SteamAchievementSetDto
{
    public int GroupId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public int? DlcAppId { get; set; }
    public bool Archived { get; set; }
    public bool? IsAchievable { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TotalAchievements { get; set; }
    public int UnlockedAchievements { get; set; }
}

public sealed class SteamAchievementProgressDto
{
    public int GameId { get; set; }
    public string Source { get; set; } = "none";
    public DateTime? LastSynced { get; set; }
    public string? LastError { get; set; }
    public string? Notice { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsFullyCompleted { get; set; }
    public bool CompletionPreserved { get; set; }
    public int TotalAchievements { get; set; }
    public int UnlockedAchievements { get; set; }
    public int EligibleAchievementsTotal { get; set; }
    public int EligibleAchievementsUnlocked { get; set; }
    public List<SteamAchievementSetDto> Sets { get; set; } = [];
    public List<SteamAchievementDto> Achievements { get; set; } = [];
}
