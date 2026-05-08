using System.Text.Json;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.DTOs.Steam;
using GamesDatabase.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Services.Steam;

public class SteamStoreService : ISteamStoreService
{
    private readonly HttpClient _httpClient;
    private readonly SteamSettings _settings;
    private readonly GamesDbContext _context;
    private readonly ILogger<SteamStoreService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SteamStoreService(IHttpClientFactory httpClientFactory, IOptions<SteamSettings> settings, GamesDbContext context, ILogger<SteamStoreService> logger)
    {
        _httpClient = httpClientFactory.CreateClient();
        _settings = settings.Value;
        _context = context;
        _logger = logger;
    }

    public async Task<SteamAppDetailsDto?> GetOrCacheAppDetailsAsync(int appId)
    {
        var cached = await _context.SteamAppCaches.FindAsync(appId);
        var ttl = TimeSpan.FromDays(_settings.AppCacheTtlDays);

        if (cached != null && DateTime.UtcNow - cached.LastFetched < ttl)
        {
            return MapToDto(cached);
        }

        var fresh = await FetchFromStoreAsync(appId);
        if (fresh == null) return cached != null ? MapToDto(cached) : null;

        if (cached == null)
        {
            _context.SteamAppCaches.Add(fresh);
        }
        else
        {
            cached.Name = fresh.Name;
            cached.Developer = fresh.Developer;
            cached.Publisher = fresh.Publisher;
            cached.GenresJson = fresh.GenresJson;
            cached.CategoriesJson = fresh.CategoriesJson;
            cached.ReleaseDate = fresh.ReleaseDate;
            cached.MetacriticScore = fresh.MetacriticScore;
            cached.HeaderImageUrl = fresh.HeaderImageUrl;
            cached.BackgroundImageUrl = fresh.BackgroundImageUrl;
            cached.Price = fresh.Price;
            cached.IsFree = fresh.IsFree;
            cached.LastFetched = fresh.LastFetched;
        }

        await _context.SaveChangesAsync();
        return MapToDto(fresh);
    }

    private async Task<SteamAppCache?> FetchFromStoreAsync(int appId)
    {
        var url = $"{_settings.StoreApiBaseUrl}/api/appdetails?appids={appId}&l=english";

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty(appId.ToString(), out var appElement)) return null;

            var wrapper = JsonSerializer.Deserialize<SteamStoreAppDetailsWrapper>(appElement.GetRawText(), _jsonOptions);
            if (wrapper?.Success != true || wrapper.Data == null) return null;

            var data = wrapper.Data;

            return new SteamAppCache
            {
                AppId = appId,
                Name = data.Name,
                IsFree = data.IsFree,
                Developer = data.Developers?.FirstOrDefault(),
                Publisher = data.Publishers?.FirstOrDefault(),
                GenresJson = data.Genres != null ? JsonSerializer.Serialize(data.Genres.Select(g => g.Description)) : null,
                CategoriesJson = data.Categories != null ? JsonSerializer.Serialize(data.Categories.Select(c => c.Description)) : null,
                ReleaseDate = data.ReleaseDate?.Date,
                MetacriticScore = data.Metacritic?.Score,
                HeaderImageUrl = data.HeaderImage,
                BackgroundImageUrl = data.Background,
                Price = data.IsFree ? "Free" : data.PriceOverview?.FinalFormatted,
                LastFetched = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching store details for AppID {AppId}", appId);
            return null;
        }
    }

    private static SteamAppDetailsDto MapToDto(SteamAppCache cache) => new()
    {
        AppId = cache.AppId,
        Name = cache.Name,
        IsFree = cache.IsFree,
        Developer = cache.Developer,
        Publisher = cache.Publisher,
        GenresJson = cache.GenresJson,
        CategoriesJson = cache.CategoriesJson,
        ReleaseDate = cache.ReleaseDate,
        MetacriticScore = cache.MetacriticScore,
        HeaderImageUrl = cache.HeaderImageUrl,
        BackgroundImageUrl = cache.BackgroundImageUrl,
        Price = cache.Price
    };

    public async Task<SteamReviewSummaryDto?> GetReviewSummaryAsync(int appId)
    {
        var url = $"https://store.steampowered.com/appreviews/{appId}?json=1&language=all&review_type=all&purchase_type=all";
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("query_summary", out var summary)) return null;

            return new SteamReviewSummaryDto
            {
                TotalPositive = summary.TryGetProperty("total_positive", out var pos) ? pos.GetInt32() : 0,
                TotalNegative = summary.TryGetProperty("total_negative", out var neg) ? neg.GetInt32() : 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching review summary for AppID {AppId}", appId);
            return null;
        }
    }
}
