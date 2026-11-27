using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts.Steam;

namespace GamesDatabase.Api.Tests.Fixtures;

/// <summary>Public synthetic catalog/player boundaries for isolated import tests;
/// no external calls, real Steam profile, key or owner data.</summary>
public sealed class MetadataSteamFixture : ISteamApiService, ISteamStoreService
{
    public const int AppId = 570;
    public const string GameName = "Autofill Steam fixture";
    public int? MetacriticScore { get; set; }
    public int ReviewSummaryCalls { get; private set; }
    public Task<List<SteamOwnedGameDto>> GetOwnedGamesAsync(string steamId) => Task.FromResult(new List<SteamOwnedGameDto> { new() { AppId = AppId, Name = GameName, PlaytimeForever = 120, Playtime2Weeks = 0 } });
    public Task<SteamPlayerAchievementsResult> GetPlayerAchievementsAsync(string steamId, int appId) => Task.FromResult(new SteamPlayerAchievementsResult { Success = true });
    public Task<SteamGameSchemaDto?> GetGameSchemaAsync(int appId) => Task.FromResult<SteamGameSchemaDto?>(new() { GameName = GameName });
    public Task<SteamAchievementSnapshot> GetAchievementSnapshotAsync(string steamId, int appId) => Task.FromResult(new SteamAchievementSnapshot { Success = true, SkipReason = "noPlayerData" });
    public Task<SteamPlayerSummaryDto?> GetPlayerSummaryAsync(string steamId) => Task.FromResult<SteamPlayerSummaryDto?>(new() { SteamId = steamId, Nickname = "Public synthetic fixture", IsPublic = true });
    public Task<SteamAppDetailsDto?> GetOrCacheAppDetailsAsync(int appId) => GetAppDetailsAsync(appId);
    public Task<SteamAppDetailsDto?> GetAppDetailsAsync(int appId, CancellationToken cancellationToken = default) => Task.FromResult<SteamAppDetailsDto?>(new() { AppId = appId, Name = GameName, MetacriticScore = MetacriticScore });
    public Task<SteamReviewSummaryDto?> GetReviewSummaryAsync(int appId)
    {
        ReviewSummaryCalls++;
        return Task.FromResult<SteamReviewSummaryDto?>(new() { TotalPositive = 95, TotalNegative = 5 });
    }
    public Task<List<SteamStoreSearchItemDto>> SearchStoreAsync(string query) => Task.FromResult(new List<SteamStoreSearchItemDto> { new() { AppId = AppId, Name = GameName, HeroUrl = Assets().HeroUrl, CoverUrl = Assets().CoverUrl } });
    public Task<string?> GetCommunityIconUrlAsync(int appId, CancellationToken cancellationToken = default) => Task.FromResult(Assets().LogoUrl);
    public Task<SteamAssetUrlsDto?> GetAssetUrlsAsync(int appId, CancellationToken cancellationToken = default) => Task.FromResult<SteamAssetUrlsDto?>(Assets());
    public Task<string?> GetLibraryCoverUrlAsync(int appId) => Task.FromResult(Assets().CoverUrl);
    private static SteamAssetUrlsDto Assets() => new()
    {
        LogoUrl = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/570/logo.png",
        HeroUrl = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/570/library_hero.jpg",
        CoverUrl = "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps/570/library_600x900.jpg"
    };
}
