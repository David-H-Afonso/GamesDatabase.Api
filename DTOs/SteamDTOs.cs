using System.Text.Json.Serialization;

namespace GamesDatabase.Api.DTOs.Steam;

// ─── Steam Web API Response Models ───────────────────────────────────────────

public class SteamOwnedGamesResponse
{
    [JsonPropertyName("response")]
    public SteamOwnedGamesData? Response { get; set; }
}

public class SteamOwnedGamesData
{
    [JsonPropertyName("game_count")]
    public int GameCount { get; set; }

    [JsonPropertyName("games")]
    public List<SteamOwnedGameRaw>? Games { get; set; }
}

public class SteamOwnedGameRaw
{
    [JsonPropertyName("appid")]
    public int AppId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("playtime_forever")]
    public int PlaytimeForever { get; set; }

    [JsonPropertyName("playtime_2weeks")]
    public int? Playtime2Weeks { get; set; }

    [JsonPropertyName("img_icon_url")]
    public string? ImgIconUrl { get; set; }
}

public class SteamPlayerAchievementsResponse
{
    [JsonPropertyName("playerstats")]
    public SteamPlayerStats? PlayerStats { get; set; }
}

public class SteamPlayerStats
{
    [JsonPropertyName("steamID")]
    public string? SteamId { get; set; }

    [JsonPropertyName("gameName")]
    public string? GameName { get; set; }

    [JsonPropertyName("achievements")]
    public List<SteamAchievementRaw>? Achievements { get; set; }

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public class SteamAchievementRaw
{
    [JsonPropertyName("apiname")]
    public string ApiName { get; set; } = string.Empty;

    [JsonPropertyName("achieved")]
    public int Achieved { get; set; }

    [JsonPropertyName("unlocktime")]
    public long UnlockTime { get; set; }
}

public class SteamGameSchemaResponse
{
    [JsonPropertyName("game")]
    public SteamGameSchemaGame? Game { get; set; }
}

public class SteamGameSchemaGame
{
    [JsonPropertyName("gameName")]
    public string? GameName { get; set; }

    [JsonPropertyName("availableGameStats")]
    public SteamAvailableGameStats? AvailableGameStats { get; set; }
}

public class SteamAvailableGameStats
{
    [JsonPropertyName("achievements")]
    public List<SteamSchemaAchievement>? Achievements { get; set; }
}

public class SteamSchemaAchievement
{
    [JsonPropertyName("name")]
    public string ApiName { get; set; } = string.Empty;

    [JsonPropertyName("defaultvalue")]
    public int DefaultValue { get; set; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("hidden")]
    public int Hidden { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    [JsonPropertyName("icongray")]
    public string? IconGray { get; set; }
}

public class SteamPlayerSummariesResponse
{
    [JsonPropertyName("response")]
    public SteamPlayerSummariesData? Response { get; set; }
}

public class SteamPlayerSummariesData
{
    [JsonPropertyName("players")]
    public List<SteamPlayerRaw>? Players { get; set; }
}

public class SteamPlayerRaw
{
    [JsonPropertyName("steamid")]
    public string SteamId { get; set; } = string.Empty;

    [JsonPropertyName("personaname")]
    public string PersonaName { get; set; } = string.Empty;

    [JsonPropertyName("avatarfull")]
    public string? AvatarFull { get; set; }

    [JsonPropertyName("profileurl")]
    public string? ProfileUrl { get; set; }

    [JsonPropertyName("communityvisibilitystate")]
    public int CommunityVisibilityState { get; set; }
}

// ─── Store API Response Models ────────────────────────────────────────────────

public class SteamStoreAppDetailsWrapper
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("data")]
    public SteamStoreAppData? Data { get; set; }
}

public class SteamStoreAppData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("is_free")]
    public bool IsFree { get; set; }

    [JsonPropertyName("developers")]
    public List<string>? Developers { get; set; }

    [JsonPropertyName("publishers")]
    public List<string>? Publishers { get; set; }

    [JsonPropertyName("genres")]
    public List<SteamStoreGenre>? Genres { get; set; }

    [JsonPropertyName("categories")]
    public List<SteamStoreCategory>? Categories { get; set; }

    [JsonPropertyName("release_date")]
    public SteamStoreReleaseDate? ReleaseDate { get; set; }

    [JsonPropertyName("metacritic")]
    public SteamStoreMetacritic? Metacritic { get; set; }

    [JsonPropertyName("header_image")]
    public string? HeaderImage { get; set; }

    [JsonPropertyName("background")]
    public string? Background { get; set; }

    [JsonPropertyName("price_overview")]
    public SteamStorePriceOverview? PriceOverview { get; set; }
}

public class SteamStoreGenre
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public class SteamStoreCategory
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public class SteamStoreReleaseDate
{
    [JsonPropertyName("coming_soon")]
    public bool ComingSoon { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }
}

public class SteamStoreMetacritic
{
    [JsonPropertyName("score")]
    public int Score { get; set; }
}

public class SteamStorePriceOverview
{
    [JsonPropertyName("final_formatted")]
    public string? FinalFormatted { get; set; }
}

// ─── DTOs for consumption ─────────────────────────────────────────────────────

public class SteamOwnedGameDto
{
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PlaytimeForever { get; set; }
    public int? Playtime2Weeks { get; set; }
    public string? IconUrl { get; set; }
}

public class SteamPlayerAchievementsResult
{
    public bool Success { get; set; }
    public bool ProfilePrivate { get; set; }
    public string? Error { get; set; }
    public List<SteamAchievementRaw> Achievements { get; set; } = new();
}

public class SteamGameSchemaDto
{
    public string? GameName { get; set; }
    public List<SteamSchemaAchievement> Achievements { get; set; } = new();
}

public class SteamPlayerSummaryDto
{
    public string SteamId { get; set; } = string.Empty;
    public string Nickname { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public string? ProfileUrl { get; set; }
    public bool IsPublic { get; set; }
}

public class SteamAppDetailsDto
{
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsFree { get; set; }
    public string? Developer { get; set; }
    public string? Publisher { get; set; }
    public string? GenresJson { get; set; }
    public string? CategoriesJson { get; set; }
    public string? ReleaseDate { get; set; }
    public int? MetacriticScore { get; set; }
    public string? HeaderImageUrl { get; set; }
    public string? BackgroundImageUrl { get; set; }
    public string? Price { get; set; }
}

// ─── Sync/Import results ──────────────────────────────────────────────────────

public class SteamSyncResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int GamesUpdated { get; set; }
    public int AchievementsUpdated { get; set; }
}

public class SteamImportResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int Created { get; set; }
    public int Linked { get; set; }
    public int Skipped { get; set; }
    public List<SteamImportedGameDto> ImportedGames { get; set; } = new();
}

public class SteamImportedGameDto
{
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? GdbGameId { get; set; }
    public string Action { get; set; } = string.Empty; // "created" | "linked" | "skipped"
}

// ─── Request DTOs ─────────────────────────────────────────────────────────────

public class SteamImportRequest
{
    public List<int> AppIds { get; set; } = new();
    public List<SteamImportGameRequest> Games { get; set; } = new();
    public bool CreateMissing { get; set; } = true;
}

public class SteamImportGameRequest
{
    public int AppId { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverUrl { get; set; }
}

public class SteamLinkGameRequest
{
    public int AppId { get; set; }
    public int GameId { get; set; }
}

public class SteamManualLinkRequest
{
    public string SteamId { get; set; } = string.Empty;
}

// ─── Response DTOs ───────────────────────────────────────────────────────────

public class SteamProfileResponse
{
    public string SteamId { get; set; } = string.Empty;
    public string SteamNickname { get; set; } = string.Empty;
    public string? SteamAvatarUrl { get; set; }
    public string? ProfileUrl { get; set; }
    public bool IsPublic { get; set; }
    public DateTime SteamLinkedAt { get; set; }
}

public class SteamAchievementDto
{
    public int Id { get; set; }
    public int SteamAppId { get; set; }
    public string ApiName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool Achieved { get; set; }
    public DateTime? UnlockTime { get; set; }
    public string? IconUrl { get; set; }
    public string? IconGrayUrl { get; set; }
    public bool Hidden { get; set; }
}

public class SteamLibraryGameDto
{
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int PlaytimeForever { get; set; }
    public int? Playtime2Weeks { get; set; }
    public string? IconUrl { get; set; }
    public int? GdbGameId { get; set; }
    public string? GdbGameName { get; set; }
}

public class SteamMatchSuggestionDto
{
    public int SteamAppId { get; set; }
    public string SteamName { get; set; } = string.Empty;
    public string? SteamIconUrl { get; set; }
    public int GdbGameId { get; set; }
    public string GdbGameName { get; set; } = string.Empty;
    public int Confidence { get; set; }
}

public class SteamReviewSummaryDto
{
    public int TotalPositive { get; set; }
    public int TotalNegative { get; set; }
    public int TotalReviews => TotalPositive + TotalNegative;
    public int ScorePercent => TotalReviews > 0 ? (int)Math.Round(100.0 * TotalPositive / TotalReviews) : 0;
}

// ─── Store Search ─────────────────────────────────────────────────────────────

public class SteamStoreSearchResponse
{
    [JsonPropertyName("total")]
    public int Total { get; set; }

    [JsonPropertyName("items")]
    public List<SteamStoreSearchItemRaw>? Items { get; set; }
}

public class SteamStoreSearchItemRaw
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("metascore")]
    public string? Metascore { get; set; }

    [JsonPropertyName("tiny_image")]
    public string? TinyImage { get; set; }

    [JsonPropertyName("price")]
    public SteamStoreSearchPriceRaw? Price { get; set; }
}

public class SteamStoreSearchPriceRaw
{
    [JsonPropertyName("final_formatted")]
    public string? FinalFormatted { get; set; }

    [JsonPropertyName("discount_percent")]
    public int DiscountPercent { get; set; }

    [JsonPropertyName("initial_formatted")]
    public string? InitialFormatted { get; set; }
}

public class SteamStoreSearchItemDto
{
    public int AppId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? CoverUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? Price { get; set; }
    public int? DiscountPercent { get; set; }
    public string? OriginalPrice { get; set; }
    public int? Metascore { get; set; }
}

public class SteamAddStoreGameRequest
{
    public int AppId { get; set; }
    public string? LogoUrl { get; set; }
    public string? CoverUrl { get; set; }
}
