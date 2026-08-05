using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GamesDatabase.Api.Application.Services;

public sealed class PlaylistService(GamesDbContext context) : IPlaylistService
{
    public async Task<IEnumerable<PlaylistSummaryDto>> GetPlaylistsAsync(int userId)
    {
        var playlists = await PlaylistQuery()
            .Where(playlist => playlist.UserId == userId)
            .OrderBy(playlist => playlist.SortOrder)
            .ThenBy(playlist => playlist.Name)
            .ToListAsync();
        return playlists.Select(playlist => playlist.ToSummaryDto()).ToList();
    }

    public async Task<PlaylistDto?> GetPlaylistByIdAsync(int id, int userId)
    {
        var playlist = await PlaylistQuery().FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        return playlist?.ToDto();
    }

    public async Task<CatalogServiceResult<PlaylistDto>> CreatePlaylistAsync(PlaylistCreateDto dto, int userId)
    {
        var name = dto.Name.Trim();
        if (name.Length == 0) return CatalogServiceResult<PlaylistDto>.BadRequest("El nombre de la playlist es obligatorio.");
        if (await context.Playlists.AnyAsync(item => item.UserId == userId && item.Name == name))
            return CatalogServiceResult<PlaylistDto>.ConflictResult($"Ya existe una playlist con el nombre '{name}'.");

        var maxOrder = await context.Playlists.Where(item => item.UserId == userId).MaxAsync(item => (int?)item.SortOrder) ?? 0;
        var playlist = new Playlist
        {
            UserId = userId,
            Name = name,
            Description = dto.Description,
            HeroUrl = dto.HeroUrl,
            CoverUrl = dto.CoverUrl,
            LogoUrl = dto.LogoUrl,
            SortOrder = maxOrder + 1
        };
        context.Playlists.Add(playlist);
        await context.SaveChangesAsync();
        return CatalogServiceResult<PlaylistDto>.Ok((await GetPlaylistByIdAsync(playlist.Id, userId))!);
    }

    public async Task<CatalogServiceResult<PlaylistDto>> UpdatePlaylistAsync(int id, PlaylistUpdateDto dto, int userId)
    {
        var playlist = await context.Playlists.FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (playlist is null) return CatalogServiceResult<PlaylistDto>.NotFoundResult("Playlist no encontrada.");

        if (dto.Name is not null)
        {
            var name = dto.Name.Trim();
            if (name.Length == 0) return CatalogServiceResult<PlaylistDto>.BadRequest("El nombre de la playlist es obligatorio.");
            if (await context.Playlists.AnyAsync(item => item.UserId == userId && item.Id != id && item.Name == name))
                return CatalogServiceResult<PlaylistDto>.ConflictResult($"Ya existe una playlist con el nombre '{name}'.");
            playlist.Name = name;
        }

        playlist.Description = dto.Description;
        playlist.HeroUrl = dto.HeroUrl;
        playlist.CoverUrl = dto.CoverUrl;
        playlist.LogoUrl = dto.LogoUrl;
        await context.SaveChangesAsync();
        return CatalogServiceResult<PlaylistDto>.Ok((await GetPlaylistByIdAsync(id, userId))!);
    }

    public async Task<CatalogServiceResult> DeletePlaylistAsync(int id, int userId)
    {
        var playlist = await context.Playlists.FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (playlist is null) return CatalogServiceResult.NotFoundResult("Playlist no encontrada.");
        context.Playlists.Remove(playlist);
        await context.SaveChangesAsync();
        await NormalizePlaylistOrdersAsync(userId);
        return CatalogServiceResult.Ok();
    }

    public async Task<CatalogServiceResult> ReorderPlaylistsAsync(ReorderPlaylistsDto dto, int userId)
    {
        if (!HasUniqueIds(dto.OrderedIds)) return CatalogServiceResult.BadRequest("OrderedIds debe contener todas las playlists una sola vez.");
        var playlists = await context.Playlists.Where(item => item.UserId == userId).ToListAsync();
        if (playlists.Count != dto.OrderedIds.Count || playlists.Any(item => !dto.OrderedIds.Contains(item.Id)))
            return CatalogServiceResult.NotFoundResult("Una o más playlists no existen.");
        foreach (var playlist in playlists) playlist.SortOrder = dto.OrderedIds.IndexOf(playlist.Id) + 1;
        await context.SaveChangesAsync();
        return CatalogServiceResult.Ok();
    }

    public async Task<CatalogServiceResult<PlaylistDto>> AddItemAsync(int id, AddPlaylistItemDto dto, int userId)
    {
        var playlist = await context.Playlists.FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (playlist is null) return CatalogServiceResult<PlaylistDto>.NotFoundResult("Playlist no encontrada.");
        var game = await context.Games.FirstOrDefaultAsync(item => item.Id == dto.GameId && item.UserId == userId);
        if (game is null) return CatalogServiceResult<PlaylistDto>.NotFoundResult("Juego no encontrado.");
        if (await context.PlaylistItems.AnyAsync(item => item.PlaylistId == id && item.GameId == dto.GameId))
            return CatalogServiceResult<PlaylistDto>.ConflictResult("El juego ya está en esta playlist.");

        var position = await context.PlaylistItems.Where(item => item.PlaylistId == id).MaxAsync(item => (int?)item.Position) ?? -1;
        context.PlaylistItems.Add(new PlaylistItem { PlaylistId = id, GameId = dto.GameId, Position = position + 1 });
        await context.SaveChangesAsync();
        return CatalogServiceResult<PlaylistDto>.Ok((await GetPlaylistByIdAsync(id, userId))!);
    }

    public async Task<CatalogServiceResult<PlaylistDto>> RemoveItemAsync(int id, int itemId, int userId)
    {
        var item = await context.PlaylistItems
            .Include(candidate => candidate.Playlist)
            .FirstOrDefaultAsync(candidate => candidate.Id == itemId && candidate.PlaylistId == id && candidate.Playlist.UserId == userId);
        if (item is null) return CatalogServiceResult<PlaylistDto>.NotFoundResult("Elemento de playlist no encontrado.");
        context.PlaylistItems.Remove(item);
        await context.SaveChangesAsync();
        await NormalizeItemPositionsAsync(id);
        return CatalogServiceResult<PlaylistDto>.Ok((await GetPlaylistByIdAsync(id, userId))!);
    }

    public async Task<CatalogServiceResult> ReorderItemsAsync(int id, ReorderPlaylistItemsDto dto, int userId)
    {
        if (!HasUniqueIds(dto.OrderedIds)) return CatalogServiceResult.BadRequest("OrderedIds debe contener todos los elementos una sola vez.");
        var playlist = await context.Playlists.Include(item => item.Items).FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (playlist is null) return CatalogServiceResult.NotFoundResult("Playlist no encontrada.");
        if (playlist.Items.Count != dto.OrderedIds.Count || playlist.Items.Any(item => !dto.OrderedIds.Contains(item.Id)))
            return CatalogServiceResult.NotFoundResult("Una o más posiciones no existen.");
        foreach (var item in playlist.Items) item.Position = dto.OrderedIds.IndexOf(item.Id);
        await context.SaveChangesAsync();
        return CatalogServiceResult.Ok();
    }

    private IQueryable<Playlist> PlaylistQuery() => context.Playlists
        .Include(playlist => playlist.Items).ThenInclude(item => item.Game).ThenInclude(game => game.Status)
        .Include(playlist => playlist.Items).ThenInclude(item => item.Game).ThenInclude(game => game.Platform)
        .Include(playlist => playlist.Items).ThenInclude(item => item.Game).ThenInclude(game => game.PlayedStatus)
        .Include(playlist => playlist.Items).ThenInclude(item => item.Game).ThenInclude(game => game.GamePlayWiths).ThenInclude(mapping => mapping.PlayWith);

    private async Task NormalizePlaylistOrdersAsync(int userId)
    {
        var playlists = await context.Playlists.Where(item => item.UserId == userId).OrderBy(item => item.SortOrder).ThenBy(item => item.Id).ToListAsync();
        for (var index = 0; index < playlists.Count; index++) playlists[index].SortOrder = index + 1;
        await context.SaveChangesAsync();
    }

    private async Task NormalizeItemPositionsAsync(int playlistId)
    {
        var items = await context.PlaylistItems.Where(item => item.PlaylistId == playlistId).OrderBy(item => item.Position).ThenBy(item => item.Id).ToListAsync();
        for (var index = 0; index < items.Count; index++) items[index].Position = index;
        await context.SaveChangesAsync();
    }

    private static bool HasUniqueIds(IReadOnlyCollection<int>? ids) => ids is { Count: > 0 } && ids.Distinct().Count() == ids.Count;
}
