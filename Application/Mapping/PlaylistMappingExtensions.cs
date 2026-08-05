using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;

namespace GamesDatabase.Api.Application.Mapping;

public static class PlaylistMappingExtensions
{
    public static PlaylistSummaryDto ToSummaryDto(this Playlist playlist)
    {
        var firstGame = playlist.Items.OrderBy(item => item.Position).FirstOrDefault()?.Game;
        return new PlaylistSummaryDto
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            HeroUrl = Resolve(playlist.HeroUrl, firstGame?.Hero),
            CoverUrl = Resolve(playlist.CoverUrl, firstGame?.Cover),
            LogoUrl = Resolve(playlist.LogoUrl, firstGame?.Logo),
            HeroUrlOverride = playlist.HeroUrl,
            CoverUrlOverride = playlist.CoverUrl,
            LogoUrlOverride = playlist.LogoUrl,
            SortOrder = playlist.SortOrder,
            GameCount = playlist.Items.Count,
            UpdatedAt = playlist.UpdatedAt
        };
    }

    public static PlaylistDto ToDto(this Playlist playlist)
    {
        var firstGame = playlist.Items.OrderBy(item => item.Position).FirstOrDefault()?.Game;
        return new PlaylistDto
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            HeroUrl = Resolve(playlist.HeroUrl, firstGame?.Hero),
            CoverUrl = Resolve(playlist.CoverUrl, firstGame?.Cover),
            LogoUrl = Resolve(playlist.LogoUrl, firstGame?.Logo),
            HeroUrlOverride = playlist.HeroUrl,
            CoverUrlOverride = playlist.CoverUrl,
            LogoUrlOverride = playlist.LogoUrl,
            SortOrder = playlist.SortOrder,
            GameCount = playlist.Items.Count,
            CreatedAt = playlist.CreatedAt,
            UpdatedAt = playlist.UpdatedAt,
            Items = playlist.Items
                .OrderBy(item => item.Position)
                .ThenBy(item => item.Id)
                .Select(item => new PlaylistItemDto
                {
                    Id = item.Id,
                    GameId = item.GameId,
                    Position = item.Position,
                    Game = item.Game.ToDto()
                })
                .ToList()
        };
    }

    private static string? Resolve(string? custom, string? fallback) => string.IsNullOrWhiteSpace(custom) ? fallback : custom;
}
