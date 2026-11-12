using GamesDatabase.Api.Application.Interfaces;
using System.Text.Json;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Contracts.Steam;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;

namespace GamesDatabase.Api.Application.Services.Steam;

public class SteamApiService : ISteamApiService
{
    private readonly HttpClient _httpClient;
    private readonly SteamSettings _settings;
    private readonly ILogger<SteamApiService> _logger;
    private readonly IMemoryCache _cache;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SteamApiService(IHttpClientFactory httpClientFactory, IOptions<SteamSettings> settings, ILogger<SteamApiService> logger, IMemoryCache cache)
    {
        _httpClient = httpClientFactory.CreateClient();
        _settings = settings.Value;
        _logger = logger;
        _cache = cache;
    }

    public async Task<List<SteamOwnedGameDto>> GetOwnedGamesAsync(string steamId)
    {
        var url = $"{_settings.ApiBaseUrl}/IPlayerService/GetOwnedGames/v1/?key={_settings.ApiKey}&steamid={steamId}&include_appinfo=1&include_played_free_games=1&format=json";

        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<SteamOwnedGamesResponse>(json, _jsonOptions);

            return data?.Response?.Games?.Select(g => new SteamOwnedGameDto
            {
                AppId = g.AppId,
                Name = g.Name ?? $"App {g.AppId}",
                PlaytimeForever = g.PlaytimeForever,
                Playtime2Weeks = g.Playtime2Weeks,
                LastPlayedAt = g.LastPlayedUnixTime.HasValue && g.LastPlayedUnixTime.Value > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(g.LastPlayedUnixTime.Value).UtcDateTime
                    : null,
                IconUrl = !string.IsNullOrEmpty(g.ImgIconUrl)
                    ? $"https://media.steampowered.com/steamcommunity/public/images/apps/{g.AppId}/{g.ImgIconUrl}.jpg"
                    : null
            }).ToList() ?? new List<SteamOwnedGameDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching owned games for SteamID {SteamId}", steamId);
            return new List<SteamOwnedGameDto>();
        }
    }

    public async Task<SteamPlayerAchievementsResult> GetPlayerAchievementsAsync(string steamId, int appId)
    {
        var url = $"{_settings.ApiBaseUrl}/ISteamUserStats/GetPlayerAchievements/v1/?key={_settings.ApiKey}&steamid={steamId}&appid={appId}&l=english&format=json";

        try
        {
            var response = await _httpClient.GetAsync(url);

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return new SteamPlayerAchievementsResult { Success = false, ProfilePrivate = true, Error = "Profile is private" };
            }

            if (!response.IsSuccessStatusCode)
                return new SteamPlayerAchievementsResult { Error = "steamUnavailable" };

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<SteamPlayerAchievementsResponse>(json, _jsonOptions);
            var stats = data?.PlayerStats;

            if (stats == null || !stats.Success)
            {
                return new SteamPlayerAchievementsResult
                {
                    Success = false,
                    Error = stats?.Error ?? "Game has no achievements or could not be retrieved"
                };
            }

            return new SteamPlayerAchievementsResult
            {
                Success = true,
                Achievements = stats.Achievements ?? new List<SteamAchievementRaw>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching achievements for SteamID {SteamId}, AppID {AppId}", steamId, appId);
            return new SteamPlayerAchievementsResult { Success = false, Error = "steamUnavailable" };
        }
    }

    public async Task<SteamGameSchemaDto?> GetGameSchemaAsync(int appId)
    {
        var url = $"{_settings.ApiBaseUrl}/ISteamUserStats/GetSchemaForGame/v2/?key={_settings.ApiKey}&appid={appId}&l=english&format=json";

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<SteamGameSchemaResponse>(json, _jsonOptions);

            if (data?.Game == null) return null;

            return new SteamGameSchemaDto
            {
                GameName = data.Game.GameName,
                Achievements = data.Game.AvailableGameStats?.Achievements ?? new List<SteamSchemaAchievement>()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching schema for AppID {AppId}", appId);
            return null;
        }
    }

    public async Task<SteamPlayerSummaryDto?> GetPlayerSummaryAsync(string steamId)
    {
        var url = $"{_settings.ApiBaseUrl}/ISteamUser/GetPlayerSummaries/v2/?key={_settings.ApiKey}&steamids={steamId}&format=json";

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<SteamPlayerSummariesResponse>(json, _jsonOptions);
            var player = data?.Response?.Players?.FirstOrDefault();

            if (player == null) return null;

            return new SteamPlayerSummaryDto
            {
                SteamId = player.SteamId,
                Nickname = player.PersonaName,
                AvatarUrl = player.AvatarFull,
                ProfileUrl = player.ProfileUrl,
                IsPublic = player.CommunityVisibilityState == 3
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching player summary for SteamID {SteamId}", steamId);
            return null;
        }
    }

    public async Task<SteamAchievementSnapshot> GetAchievementSnapshotAsync(string steamId, int appId)
    {
        var cacheKey = $"steam-achievement-schema:{appId}:english";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            SteamSetSchema? schema = _cache.Get<SteamSetSchema>(cacheKey);
            if (schema == null)
            {
                var schemaResult = await GetServiceResponseAsync<SteamSetSchema>("GetGameAchievements", new { appid = appId, language = "english" }, appId);
                if (schemaResult.Unsupported)
                    return await GetLegacyAchievementSnapshotAsync(steamId, appId);
                if (schemaResult.Result == 42)
                    return new SteamAchievementSnapshot { Success = true, SupportsSets = true, NoAchievements = true };
                if (schemaResult.Result == 2)
                    return await ClassifyUnavailableStatsAsync(appId);
                if (!schemaResult.Success || !IsValidSchema(schemaResult.Data))
                    return new SteamAchievementSnapshot { Error = schemaResult.Error ?? "invalidSchema" };
                schema = schemaResult.Data!;
                schema.Achievements ??= [];
                _cache.Set(cacheKey, schema, TimeSpan.FromMinutes(5));
            }

            // Protobuf JSON omits an empty repeated field. It is empty only when
            // the validated group counts also confirm zero definitions.
            if (schema.Achievements!.Count == 0)
                return new SteamAchievementSnapshot { Success = true, SupportsSets = true, NoAchievements = true, Schema = schema };

            var playerResult = await GetServiceResponseAsync<SteamSetPlayerProgress>("GetUserAchievements", new { steamid = steamId, appid = appId }, appId);
            if (playerResult.Unsupported)
                return await GetLegacyAchievementSnapshotAsync(steamId, appId);
            if (playerResult.Result == 2)
                return await ClassifyUnavailableStatsAsync(appId);
            if (!playerResult.Success)
                return new SteamAchievementSnapshot { Error = playerResult.Error ?? "playerUnavailable" };

            var player = playerResult.Data;
            if (player?.SchemaVersion == null || player.SchemaHash == null)
                return new SteamAchievementSnapshot { Error = "invalidPlayerSnapshot" };
            if (schema.SchemaVersion != player.SchemaVersion || schema.SchemaHash != player.SchemaHash)
            {
                _cache.Remove(cacheKey);
                continue;
            }

            var unlocks = player.Achievements ?? [];
            if (player.Groups == null || player.Groups.Count == 0)
                return unlocks.Count == 0
                    ? new SteamAchievementSnapshot { SkipReason = "noPlayerData" }
                    : new SteamAchievementSnapshot { Error = "invalidPlayerSnapshot" };
            var keys = schema.Achievements!.Select(achievement => achievement.InternalKey).ToHashSet();
            if (unlocks.Select(achievement => achievement.InternalKey).Distinct().Count() != unlocks.Count ||
                unlocks.Any(achievement => !keys.Contains(achievement.InternalKey)) ||
                unlocks.Any(achievement => achievement.Unlocked == null && achievement.ProgressInt == null && achievement.ProgressFloat == null) ||
                unlocks.Any(achievement => (achievement.ProgressInt.HasValue && !double.IsFinite(achievement.ProgressInt.Value)) ||
                    (achievement.ProgressFloat.HasValue && !double.IsFinite(achievement.ProgressFloat.Value))) ||
                player.Groups.Select(group => group.GroupId).Distinct().Count() != player.Groups.Count ||
                player.Groups.Any(group => !schema.Groups!.Any(definition => definition.GroupId == group.GroupId)))
                return new SteamAchievementSnapshot { Error = "invalidPlayerSnapshot" };

            return new SteamAchievementSnapshot { Success = true, SupportsSets = true, Schema = schema, Player = player };
        }

        return new SteamAchievementSnapshot { Error = "schemaChanged" };
    }

    private static bool IsValidSchema(SteamSetSchema? schema)
    {
        if (schema?.Groups is not { Count: > 0 }) return false;
        var achievements = schema.Achievements ?? [];
        return
        schema.SchemaVersion.HasValue && schema.SchemaHash.HasValue &&
        schema.Groups.All(group => group.TotalAchievements >= 0 && group.CompletionAchievements >= 0 &&
            group.CompletionAchievements <= group.TotalAchievements &&
            achievements.Count(achievement => achievement.GroupId == group.GroupId) == group.TotalAchievements) &&
        achievements.All(achievement => !string.IsNullOrWhiteSpace(achievement.ApiName) &&
            schema.Groups.Any(group => group.GroupId == achievement.GroupId)) &&
        achievements.Select(achievement => achievement.ApiName).Distinct(StringComparer.Ordinal).Count() == achievements.Count &&
        achievements.Select(achievement => achievement.InternalKey).Distinct().Count() == achievements.Count &&
        schema.Groups.Select(group => group.GroupId).Distinct().Count() == schema.Groups.Count;
    }

    private async Task<SteamAchievementSnapshot> ClassifyUnavailableStatsAsync(int appId)
    {
        var key = $"steam-coming-soon:{appId}";
        if (_cache.TryGetValue<bool>(key, out var cached))
            return cached ? new() { SkipReason = "unreleased" } : new() { Error = "statsUnavailable" };
        try
        {
            using var response = await _httpClient.GetAsync($"{_settings.StoreApiBaseUrl.TrimEnd('/')}/api/appdetails?appids={appId}&filters=release_date&l=english");
            if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    document.RootElement.TryGetProperty(appId.ToString(System.Globalization.CultureInfo.InvariantCulture), out var app) && app.ValueKind == JsonValueKind.Object &&
                    app.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True &&
                    app.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("release_date", out var release) && release.ValueKind == JsonValueKind.Object &&
                    release.TryGetProperty("coming_soon", out var comingSoon) && comingSoon.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    var upcoming = comingSoon.GetBoolean();
                    _cache.Set(key, upcoming, TimeSpan.FromMinutes(5));
                    return upcoming ? new() { SkipReason = "unreleased" } : new() { Error = "statsUnavailable" };
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("Steam release-state lookup failed for AppID {AppId} ({FailureType})", appId, ex.GetType().Name);
        }
        // Do not guess that a game is unreleased, or silently hide a provider failure.
        return new() { Error = "statsUnavailable" };
    }

    private async Task<SteamAchievementSnapshot> GetLegacyAchievementSnapshotAsync(string steamId, int appId)
    {
        var player = await GetPlayerAchievementsAsync(steamId, appId);
        if (!player.Success)
            return new SteamAchievementSnapshot { Error = player.ProfilePrivate ? "profilePrivate" : "steamUnavailable" };
        var schema = await GetGameSchemaAsync(appId);
        if (schema == null || (player.Achievements.Count > 0 && schema.Achievements.Count == 0))
            return new SteamAchievementSnapshot { Error = "invalidSchema" };
        return new SteamAchievementSnapshot
        {
            Success = true,
            NoAchievements = schema.Achievements.Count == 0 && player.Achievements.Count == 0,
            LegacySchema = schema,
            LegacyAchievements = player.Achievements
        };
    }

    private async Task<ServiceResponse<T>> GetServiceResponseAsync<T>(string method, object input, int appId) where T : class
    {
        var jsonInput = Uri.EscapeDataString(JsonSerializer.Serialize(input));
        var url = $"{_settings.ApiBaseUrl}/IPlayerService/{method}/v1/?input_json={jsonInput}";
        try
        {
            using var response = await _httpClient.GetAsync(url);
            var unsupported = response.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.MethodNotAllowed or System.Net.HttpStatusCode.NotImplemented;
            if (!response.IsSuccessStatusCode)
                return new(false, unsupported, null, default, response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Forbidden => "profilePrivate",
                    System.Net.HttpStatusCode.TooManyRequests => "rateLimited",
                    _ => "steamUnavailable"
                });

            int? result = response.Headers.TryGetValues("x-eresult", out var values) && int.TryParse(values.FirstOrDefault(), out var parsed) ? parsed : null;
            if (result.HasValue && result != 1)
                return new(false, false, result, default, result == 15 ? "profilePrivate" : "statsUnavailable");
            var json = await response.Content.ReadAsStringAsync();
            var envelope = JsonSerializer.Deserialize<SteamServiceEnvelope<T>>(json, _jsonOptions);
            return new(envelope?.Response != null, false, result, envelope == null ? default : envelope.Response, envelope?.Response == null ? "invalidResponse" : null);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("Steam achievement method {Method} could not decode AppID {AppId} at {JsonPath}", method, appId, ex.Path ?? "unknown");
            return new(false, false, null, default, "invalidResponse");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning("Steam achievement method {Method} failed for AppID {AppId} ({FailureType})", method, appId, ex.GetType().Name);
            return new(false, false, null, default, "steamUnavailable");
        }
    }

    private sealed record ServiceResponse<T>(bool Success, bool Unsupported, int? Result, T? Data, string? Error) where T : class;
}
