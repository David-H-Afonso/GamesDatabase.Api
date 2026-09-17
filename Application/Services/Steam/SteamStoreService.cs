using GamesDatabase.Api.Application.Interfaces;
using System.Text.Json;
using System.Text.RegularExpressions;
using GamesDatabase.Api.Configuration;
using GamesDatabase.Api.Infrastructure.Persistence;
using GamesDatabase.Api.Contracts.Steam;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GamesDatabase.Api.Application.Services.Steam;

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

    // Steam serves community/library icons from two different CDN path patterns.
    // Old format:  .../steamcommunity/public/images/apps/{appId}/{hash}.jpg
    // New format:  .../community_assets/images/apps/{appId}/{hash}.jpg  (Akamai CDN, ~2025)
    private static readonly Regex CommunityIconUrlRegex = new(
        @"https?://[^""'\s<>]+/(?:steamcommunity/public/images|community_assets/images)/apps/(?<appId>\d+)/(?<hash>[a-f0-9]{32,64})\.jpg",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
                ReleaseDate = GameDateNormalizer.NormalizeSteamReleaseDate(data.ReleaseDate?.Date),
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
        ReleaseDate = GameDateNormalizer.NormalizeSteamReleaseDate(cache.ReleaseDate),
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

    public async Task<List<SteamStoreSearchItemDto>> SearchStoreAsync(string query)
    {
        var encodedQuery = Uri.EscapeDataString(query);
        var url = $"https://store.steampowered.com/api/storesearch/?term={encodedQuery}&l=english&cc=US";
        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return [];

            var json = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<SteamStoreSearchResponse>(json, _jsonOptions);
            if (result?.Items == null) return [];

            var items = new List<SteamStoreSearchItemDto>();
            foreach (var item in result.Items)
            {
                var assets = await GetAssetUrlsAsync(item.Id);
                items.Add(new SteamStoreSearchItemDto
                {
                    AppId = item.Id,
                    Name = item.Name,
                    HeroUrl = assets?.HeroUrl,
                    CoverUrl = assets?.CoverUrl,
                    LogoUrl = assets?.LogoUrl ?? item.TinyImage,
                    Price = item.Price?.FinalFormatted,
                    DiscountPercent = item.Price?.DiscountPercent > 0 ? item.Price.DiscountPercent : null,
                    OriginalPrice = item.Price?.DiscountPercent > 0 ? item.Price.InitialFormatted : null,
                    Metascore = int.TryParse(item.Metascore, out var ms) && ms > 0 ? ms : null
                });
            }

            return items;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching Steam Store for '{Query}'", query);
            return [];
        }
    }

    public async Task<string?> GetCommunityIconUrlAsync(int appId)
    {
        var url = $"https://store.steampowered.com/app/{appId}/?l=english";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Cookie", "birthtime=568022401; lastagecheckage=1-0-1988; wants_mature_content=1");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode) return null;

            var html = await response.Content.ReadAsStringAsync();
            var appIconIndex = html.IndexOf("apphub_AppIcon", StringComparison.OrdinalIgnoreCase);
            if (appIconIndex >= 0)
            {
                var iconHtml = html.Substring(appIconIndex, Math.Min(1200, html.Length - appIconIndex));
                var appIconUrl = FindCommunityIconUrl(iconHtml, appId);
                if (appIconUrl != null) return appIconUrl;
            }

            return FindCommunityIconUrl(html, appId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching Steam community icon for AppID {AppId}", appId);
            return null;
        }
    }

    public async Task<SteamAssetUrlsDto?> GetAssetUrlsAsync(int appId)
    {
        var requestJson = JsonSerializer.Serialize(new
        {
            ids = new[] { new { appid = appId } },
            context = new { language = 0, country_code = "ES" },
            data_request = new { include_assets = true }
        });
        var url = $"https://api.steampowered.com/IStoreBrowseService/GetItems/v1/?input_json={Uri.EscapeDataString(requestJson)}";

        try
        {
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!document.RootElement.TryGetProperty("response", out var responseElement)
                || !responseElement.TryGetProperty("store_items", out var storeItems)
                || storeItems.ValueKind != JsonValueKind.Array
                || storeItems.GetArrayLength() == 0)
            {
                return null;
            }

            var item = storeItems[0];
            if (!item.TryGetProperty("assets", out var assets)
                || !assets.TryGetProperty("asset_url_format", out var formatElement))
            {
                return null;
            }

            var format = formatElement.GetString();
            if (string.IsNullOrWhiteSpace(format)) return null;

            string? BuildAsset(string property) => assets.TryGetProperty(property, out var element)
                ? BuildAssetUrl(format, element.GetString())
                : null;

            var communityIcon = assets.TryGetProperty("community_icon", out var iconElement) ? iconElement.GetString() : null;
            return new SteamAssetUrlsDto
            {
                HeroUrl = BuildAsset("header_2x") ?? BuildAsset("header"),
                CoverUrl = BuildAsset("library_capsule_2x") ?? BuildAsset("library_capsule"),
                LogoUrl = string.IsNullOrWhiteSpace(communityIcon)
                    ? null
                    : $"https://shared.akamai.steamstatic.com/community_assets/images/apps/{appId}/{communityIcon}.jpg"
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching Steam library assets for AppID {AppId}", appId);
            return null;
        }
    }

    public async Task<string?> GetLibraryCoverUrlAsync(int appId)
    {
        return (await GetAssetUrlsAsync(appId))?.CoverUrl;
    }

    private static string? BuildAssetUrl(string format, string? assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath)) return null;
        return $"https://shared.akamai.steamstatic.com/store_item_assets/{format.Replace("${FILENAME}", assetPath, StringComparison.Ordinal)}";
    }

    private static string? FindCommunityIconUrl(string html, int appId)
    {
        var matches = CommunityIconUrlRegex.Matches(html);

        foreach (Match match in matches)
        {
            if (!match.Success) continue;
            if (!int.TryParse(match.Groups["appId"].Value, out var matchedAppId)) continue;
            if (matchedAppId != appId) continue;

            return match.Value.Replace("&amp;", "&");
        }

        return null;
    }
}
