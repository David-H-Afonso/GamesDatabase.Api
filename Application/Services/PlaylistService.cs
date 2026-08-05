using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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
        foreach (var playlist in playlists) await HydrateAutomaticItemsAsync(playlist, userId);
        return playlists.Select(playlist => playlist.ToSummaryDto()).ToList();
    }

    public async Task<PlaylistDto?> GetPlaylistByIdAsync(int id, int userId)
    {
        var playlist = await PlaylistQuery().FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (playlist is not null) await HydrateAutomaticItemsAsync(playlist, userId);
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
            SortOrder = maxOrder + 1,
            IsAutomatic = dto.IsAutomatic,
            RulesJson = dto.IsAutomatic ? JsonSerializer.Serialize(dto.Rules ?? new PlaylistRulesDto()) : null
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
        if (dto.IsAutomatic.HasValue) playlist.IsAutomatic = dto.IsAutomatic.Value;
        if (dto.Rules is not null || dto.IsAutomatic == false) playlist.RulesJson = playlist.IsAutomatic ? JsonSerializer.Serialize(dto.Rules ?? new PlaylistRulesDto()) : null;
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
        if (playlist.IsAutomatic) return CatalogServiceResult<PlaylistDto>.BadRequest("Las playlists automáticas se llenan mediante sus reglas.");
        var game = await context.Games.FirstOrDefaultAsync(item => item.Id == dto.GameId && item.UserId == userId);
        if (game is null) return CatalogServiceResult<PlaylistDto>.NotFoundResult("Juego no encontrado.");
        if (await context.PlaylistItems.AnyAsync(item => item.PlaylistId == id && item.GameId == dto.GameId))
            return CatalogServiceResult<PlaylistDto>.ConflictResult("El juego ya está en esta playlist.");

        var position = await context.PlaylistItems.Where(item => item.PlaylistId == id).MaxAsync(item => (int?)item.Position) ?? -1;
        context.PlaylistItems.Add(new PlaylistItem { PlaylistId = id, GameId = dto.GameId, Position = position + 1 });
        playlist.UpdatedAt = DateTime.UtcNow;
        playlist.ModifiedSinceExport = true;
        await context.SaveChangesAsync();
        return CatalogServiceResult<PlaylistDto>.Ok((await GetPlaylistByIdAsync(id, userId))!);
    }

    public async Task<CatalogServiceResult<PlaylistDto>> RemoveItemAsync(int id, int itemId, int userId)
    {
        var item = await context.PlaylistItems
            .Include(candidate => candidate.Playlist)
            .FirstOrDefaultAsync(candidate => candidate.Id == itemId && candidate.PlaylistId == id && candidate.Playlist.UserId == userId);
        if (item is null) return CatalogServiceResult<PlaylistDto>.NotFoundResult("Elemento de playlist no encontrado.");
        if (item.Playlist.IsAutomatic) return CatalogServiceResult<PlaylistDto>.BadRequest("Las playlists automáticas se llenan mediante sus reglas.");
        context.PlaylistItems.Remove(item);
        item.Playlist.UpdatedAt = DateTime.UtcNow;
        item.Playlist.ModifiedSinceExport = true;
        await context.SaveChangesAsync();
        await NormalizeItemPositionsAsync(id);
        return CatalogServiceResult<PlaylistDto>.Ok((await GetPlaylistByIdAsync(id, userId))!);
    }

    public async Task<CatalogServiceResult> ReorderItemsAsync(int id, ReorderPlaylistItemsDto dto, int userId)
    {
        if (!HasUniqueIds(dto.OrderedIds)) return CatalogServiceResult.BadRequest("OrderedIds debe contener todos los elementos una sola vez.");
        var playlist = await context.Playlists.Include(item => item.Items).FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (playlist is null) return CatalogServiceResult.NotFoundResult("Playlist no encontrada.");
        if (playlist.IsAutomatic) return CatalogServiceResult.BadRequest("Las playlists automáticas se ordenan mediante sus reglas.");
        if (playlist.Items.Count != dto.OrderedIds.Count || playlist.Items.Any(item => !dto.OrderedIds.Contains(item.Id)))
            return CatalogServiceResult.NotFoundResult("Una o más posiciones no existen.");
        foreach (var item in playlist.Items) item.Position = dto.OrderedIds.IndexOf(item.Id);
        playlist.UpdatedAt = DateTime.UtcNow;
        playlist.ModifiedSinceExport = true;
        await context.SaveChangesAsync();
        return CatalogServiceResult.Ok();
    }

    public async Task<CatalogServiceResult<PlaylistTransferDto>> ExportPlaylistAsync(int id, PlaylistExportReference reference, int userId)
    {
        var playlist = await PlaylistQuery().FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (playlist is null) return CatalogServiceResult<PlaylistTransferDto>.NotFoundResult("Playlist no encontrada.");

        var transfer = new PlaylistTransferDto
        {
            Name = playlist.Name,
            Description = playlist.Description,
            HeroUrl = playlist.HeroUrl,
            CoverUrl = playlist.CoverUrl,
            LogoUrl = playlist.LogoUrl,
            Games = playlist.Items
                .OrderBy(item => item.Position)
                .ThenBy(item => item.Id)
                .Select(item => reference == PlaylistExportReference.Id
                    ? new PlaylistGameReferenceDto { GameId = item.GameId }
                    : new PlaylistGameReferenceDto { Name = item.Game.Name })
                .ToList()
        };
        return CatalogServiceResult<PlaylistTransferDto>.Ok(transfer);
    }

    public async Task<CatalogServiceResult<PlaylistDto>> ImportPlaylistAsync(PlaylistTransferDto dto, int userId)
    {
        var name = dto.Name.Trim();
        if (!string.Equals(dto.Format, "games-database-playlist", StringComparison.OrdinalIgnoreCase)
            || dto.Version != 1)
        {
            return CatalogServiceResult<PlaylistDto>.BadRequest("El JSON no es una playlist de Games Database compatible.");
        }

        if (name.Length == 0) return CatalogServiceResult<PlaylistDto>.BadRequest("El nombre de la playlist es obligatorio.");
        if (await context.Playlists.AnyAsync(item => item.UserId == userId && item.Name == name))
            return CatalogServiceResult<PlaylistDto>.ConflictResult($"Ya existe una playlist con el nombre '{name}'.");
        if (dto.Games.Count != dto.Games.Count(reference => reference.GameId.HasValue || !string.IsNullOrEmpty(reference.Name)))
            return CatalogServiceResult<PlaylistDto>.BadRequest("Cada juego debe incluir gameId o name.");

        var ids = dto.Games.Where(reference => reference.GameId.HasValue).Select(reference => reference.GameId!.Value).Distinct().ToList();
        var names = dto.Games.Where(reference => !reference.GameId.HasValue && !string.IsNullOrEmpty(reference.Name)).Select(reference => reference.Name!).Distinct().ToList();
        var games = await context.Games
            .Where(game => game.UserId == userId && (ids.Contains(game.Id) || names.Contains(game.Name)))
            .ToListAsync();
        var gamesById = games.ToDictionary(game => game.Id);
        var gamesByName = games.GroupBy(game => game.Name).ToDictionary(group => group.Key, group => group.Single());
        var resolvedGames = new List<Game>();
        var missing = new List<string>();
        foreach (var reference in dto.Games)
        {
            Game? game = null;
            if (reference.GameId.HasValue
                && gamesById.TryGetValue(reference.GameId.Value, out var gameById)
                && (string.IsNullOrEmpty(reference.Name) || gameById.Name == reference.Name))
            {
                game = gameById;
            }
            if (game is null && !string.IsNullOrEmpty(reference.Name)) gamesByName.TryGetValue(reference.Name, out game);
            if (game is null)
            {
                missing.Add(reference.GameId.HasValue ? $"gameId {reference.GameId.Value}" : $"name '{reference.Name}'");
                continue;
            }

            if (resolvedGames.Any(item => item.Id == game.Id))
                return CatalogServiceResult<PlaylistDto>.BadRequest($"El juego '{game.Name}' aparece más de una vez en el JSON.");
            resolvedGames.Add(game);
        }

        if (missing.Count > 0)
            return CatalogServiceResult<PlaylistDto>.NotFoundResult($"No se pudieron resolver: {string.Join(", ", missing)}.");

        var maxOrder = await context.Playlists.Where(item => item.UserId == userId).MaxAsync(item => (int?)item.SortOrder) ?? 0;
        var playlist = new Playlist
        {
            UserId = userId,
            Name = name,
            Description = dto.Description,
            HeroUrl = dto.HeroUrl,
            CoverUrl = dto.CoverUrl,
            LogoUrl = dto.LogoUrl,
            SortOrder = maxOrder + 1,
            Items = resolvedGames.Select((game, position) => new PlaylistItem { GameId = game.Id, Position = position }).ToList()
        };
        context.Playlists.Add(playlist);
        await context.SaveChangesAsync();
        return CatalogServiceResult<PlaylistDto>.Ok((await GetPlaylistByIdAsync(playlist.Id, userId))!);
    }

    private IQueryable<Playlist> PlaylistQuery() => context.Playlists
        .Include(playlist => playlist.Items).ThenInclude(item => item.Game).ThenInclude(game => game.Status)
        .Include(playlist => playlist.Items).ThenInclude(item => item.Game).ThenInclude(game => game.Platform)
        .Include(playlist => playlist.Items).ThenInclude(item => item.Game).ThenInclude(game => game.PlayedStatus)
        .Include(playlist => playlist.Items).ThenInclude(item => item.Game).ThenInclude(game => game.GamePlayWiths).ThenInclude(mapping => mapping.PlayWith);

    private async Task HydrateAutomaticItemsAsync(Playlist playlist, int userId)
    {
        if (!playlist.IsAutomatic) return;
        var rules = string.IsNullOrWhiteSpace(playlist.RulesJson)
            ? new PlaylistRulesDto()
            : JsonSerializer.Deserialize<PlaylistRulesDto>(playlist.RulesJson) ?? new PlaylistRulesDto();
        var query = context.Games.AsNoTracking()
            .Include(game => game.Status)
            .Include(game => game.Platform)
            .Include(game => game.PlayedStatus)
            .Include(game => game.GamePlayWiths).ThenInclude(mapping => mapping.PlayWith)
            .Where(game => game.UserId == userId);
        if (!string.IsNullOrWhiteSpace(rules.Search)) query = query.Where(game => game.Name.Contains(rules.Search));
        if (rules.StatusIds.Count > 0) query = query.Where(game => rules.StatusIds.Contains(game.StatusId));
        if (rules.PlatformIds.Count > 0) query = query.Where(game => game.PlatformId.HasValue && rules.PlatformIds.Contains(game.PlatformId.Value));
        if (rules.PlayedStatusIds.Count > 0) query = query.Where(game => game.PlayedStatusId.HasValue && rules.PlayedStatusIds.Contains(game.PlayedStatusId.Value));
        if (rules.Favorite.HasValue) query = query.Where(game => game.Favorite == rules.Favorite.Value);
        if (rules.MinGrade.HasValue) query = query.Where(game => game.Grade >= rules.MinGrade.Value);
        if (rules.MaxGrade.HasValue) query = query.Where(game => game.Grade <= rules.MaxGrade.Value);
        if (rules.MinCritic.HasValue) query = query.Where(game => game.Critic >= rules.MinCritic.Value);
        if (rules.MaxCritic.HasValue) query = query.Where(game => game.Critic <= rules.MaxCritic.Value);
        if (rules.MinScore.HasValue) query = query.Where(game => game.Score >= rules.MinScore.Value);
        if (rules.MaxScore.HasValue) query = query.Where(game => game.Score <= rules.MaxScore.Value);
        if (rules.HasSteam.HasValue) query = rules.HasSteam.Value ? query.Where(game => game.SteamAppId.HasValue) : query.Where(game => !game.SteamAppId.HasValue);
        if (rules.FullCompletion.HasValue) query = rules.FullCompletion.Value ? query.Where(game => game.Completion == 100 || game.IsManuallyCompleted) : query.Where(game => game.Completion != 100 && !game.IsManuallyCompleted);
        var games = await query.ToListAsync();
        IEnumerable<Game> ordered = rules.SortBy.ToLowerInvariant() switch
        {
            "name" => games.OrderBy(game => game.Name),
            "grade" => games.OrderBy(game => game.Grade),
            "critic" => games.OrderBy(game => game.Critic),
            "score" => games.OrderBy(game => game.Score),
            "released" => games.OrderBy(game => game.Released),
            "updated" => games.OrderBy(game => game.UpdatedAt),
            _ => games.OrderBy(game => game.Id)
        };
        if (rules.SortDescending) ordered = ordered.Reverse();
        if (rules.Limit is > 0) ordered = ordered.Take(rules.Limit.Value);
        playlist.Items = ordered.Select((game, position) => new PlaylistItem { Id = game.Id, PlaylistId = playlist.Id, GameId = game.Id, Position = position, Game = game, Playlist = playlist }).ToList();
    }

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
