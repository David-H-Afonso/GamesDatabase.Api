using GamesDatabase.Api.Contracts.Steam;

namespace GamesDatabase.Api.Application.Interfaces;

public interface ISteamProfileService
{
    Task<SteamProfileResponse?> GetProfileAsync(int userId);
    Task UnlinkSteamAsync(int userId);
    Task<SteamProfileResponse?> LinkSteamManuallyAsync(int userId, string rawSteamId);
    Task<(bool Success, string? Error, object? Result)> LinkGameAsync(int userId, SteamLinkGameRequest request);
    Task<List<SteamLibraryGameDto>?> GetLibraryAsync(int userId);
    Task<List<SteamAchievementDto>> GetAchievementsAsync(int userId, int gameId);
    Task<List<SteamMatchSuggestionDto>> GetMatchSuggestionsAsync(int userId);
    Task<List<SteamMatchSuggestionDto>> GetStoreMatchSuggestionsAsync(int userId);
    Task<(int Dismissed, string? Error)> DismissMatchSuggestionsAsync(int userId, SteamDismissMatchSuggestionsRequest request);
    Task<List<SteamDateSuggestionDto>?> GetDateSuggestionsAsync(int userId, int? gameId, bool includeStarted);
    Task<SteamApplyDateSuggestionsResponse?> ApplyDateSuggestionsAsync(int userId, SteamApplyDateSuggestionsRequest request);
    Task<(int Dismissed, string? Error)> DismissDateSuggestionsAsync(int userId, SteamDismissDateSuggestionsRequest request);
    Task<(bool Created, string? SteamId, string? SteamNickname, string? SteamAvatarUrl)> LinkSteamAccountAsync(int userId, string steamId);
}
