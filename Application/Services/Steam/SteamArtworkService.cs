using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Contracts.Steam;

namespace GamesDatabase.Api.Application.Services.Steam;

/// <summary>Resolves candidates, never clears/saves a game or starts a player-data sync.</summary>
public sealed class SteamArtworkService(ISteamStoreService store) : ISteamArtworkService
{
    public async Task<SteamAssetUrlsDto?> GetAsync(int appId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appId);
        cancellationToken.ThrowIfCancellationRequested();
        var assets = await store.GetAssetUrlsAsync(appId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var result = new SteamAssetUrlsDto
        {
            LogoUrl = Candidate(assets?.LogoUrl),
            HeroUrl = Candidate(assets?.HeroUrl),
            CoverUrl = Candidate(assets?.CoverUrl)
        };
        if (result.LogoUrl is null)
        {
            result.LogoUrl = Candidate(await store.GetCommunityIconUrlAsync(appId, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
        }
        if (result.HeroUrl is null)
        {
            result.HeroUrl = Candidate((await store.GetAppDetailsAsync(appId, cancellationToken))?.HeaderImageUrl);
            cancellationToken.ThrowIfCancellationRequested();
        }
        // A missing role stays missing. Cover must not silently become Hero,
        // nor may a modern content-hashed CDN path be guessed from an App ID.
        return result.LogoUrl is null && result.HeroUrl is null && result.CoverUrl is null ? null : result;
    }

    private static string? Candidate(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "https" or "http" && string.IsNullOrEmpty(uri.UserInfo)
            ? uri.AbsoluteUri : null;
}
