using System.Text.Json;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.DTOs.Steam;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Services.Steam;

public class SteamApiService : ISteamApiService
{
    private readonly HttpClient _httpClient;
    private readonly SteamSettings _settings;
    private readonly ILogger<SteamApiService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SteamApiService(IHttpClientFactory httpClientFactory, IOptions<SteamSettings> settings, ILogger<SteamApiService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _settings = settings.Value;
        _logger = logger;
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
            return new SteamPlayerAchievementsResult { Success = false, Error = ex.Message };
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
}
