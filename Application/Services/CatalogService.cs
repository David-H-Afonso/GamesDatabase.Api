using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Common;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;

namespace GamesDatabase.Api.Application.Services;

public class CatalogService : ICatalogService
{
    private readonly GamesDbContext _context;

    public CatalogService(GamesDbContext context)
    {
        _context = context;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // STATUSES
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<PagedResult<GameStatusDto>> GetStatusesAsync(QueryParameters parameters, int userId)
    {
        var query = _context.GameStatuses.Where(s => s.UserId == userId).AsQueryable();

        if (!string.IsNullOrEmpty(parameters.Search))
        {
            query = query.Where(s => s.Name.Contains(parameters.Search));
        }

        if (parameters.IsActive.HasValue)
        {
            query = query.Where(s => s.IsActive == parameters.IsActive.Value);
        }

        if (!string.IsNullOrEmpty(parameters.SortBy))
        {
            query = parameters.SortBy.ToLower() switch
            {
                "name" or "alphabetical" => parameters.SortDescending ?
                    query.OrderBy(s => EF.Functions.Collate(s.Name, "NOCASE")).Reverse() :
                    query.OrderBy(s => EF.Functions.Collate(s.Name, "NOCASE")),
                "isactive" => parameters.SortDescending ? query.OrderByDescending(s => s.IsActive) : query.OrderBy(s => s.IsActive),
                "creation" or "id" => parameters.SortDescending ? query.OrderByDescending(s => s.Id) : query.OrderBy(s => s.Id),
                "sortorder" or "order" or "position" => parameters.SortDescending ? query.OrderByDescending(s => s.SortOrder) : query.OrderBy(s => s.SortOrder),
                _ => query.OrderBy(s => s.SortOrder).ThenBy(s => EF.Functions.Collate(s.Name, "NOCASE"))
            };
        }
        else
        {
            query = query.OrderBy(s => s.SortOrder).ThenBy(s => EF.Functions.Collate(s.Name, "NOCASE"));
        }

        var totalCount = await query.CountAsync();

        var statuses = await query
            .Skip(parameters.Skip)
            .Take(parameters.Take)
            .ToListAsync();

        var statusDtos = statuses.Select(s => s.ToDto()).ToList();

        return new PagedResult<GameStatusDto>
        {
            Data = statusDtos,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        };
    }

    public async Task<IEnumerable<GameStatusDto>> GetActiveStatusesAsync(int userId)
    {
        var statuses = await _context.GameStatuses
            .Where(s => s.IsActive && s.UserId == userId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => EF.Functions.Collate(s.Name, "NOCASE"))
            .ToListAsync();

        return statuses.Select(s => s.ToDto());
    }

    public async Task<IEnumerable<object>> GetOrderedStatusesAsync(int userId)
    {
        var statuses = await _context.GameStatuses
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => EF.Functions.Collate(s.Name, "NOCASE"))
            .Select(s => new { s.Id, s.Name, s.SortOrder })
            .ToListAsync();

        return statuses;
    }

    public async Task<GameStatusDto?> GetStatusByIdAsync(int id, int userId)
    {
        var gameStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        return gameStatus?.ToDto();
    }

    public async Task<CatalogServiceResult<GameStatusDto>> CreateStatusAsync(GameStatusCreateDto createDto, int userId)
    {
        if (await _context.GameStatuses.AnyAsync(s => s.Name.ToLower() == createDto.Name.ToLower() && s.UserId == userId))
        {
            return CatalogServiceResult<GameStatusDto>.ConflictResult("Ya existe un estado con este nombre");
        }

        var maxSort = await _context.GameStatuses
            .Where(s => s.UserId == userId)
            .MaxAsync(s => (int?)s.SortOrder) ?? 0;

        var gameStatus = new GameStatus
        {
            UserId = userId,
            Name = createDto.Name,
            SortOrder = createDto.SortOrder ?? (maxSort + 1),
            IsActive = createDto.IsActive,
            Color = createDto.Color
        };

        _context.GameStatuses.Add(gameStatus);
        await _context.SaveChangesAsync();

        return CatalogServiceResult<GameStatusDto>.Ok(gameStatus.ToDto());
    }

    public async Task<CatalogServiceResult> UpdateStatusAsync(int id, GameStatusUpdateDto updateDto, int userId)
    {
        var gameStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (gameStatus == null)
            return CatalogServiceResult.NotFoundResult("Estado no encontrado");

        if (await _context.GameStatuses.AnyAsync(s => s.Name.ToLower() == updateDto.Name.ToLower() && s.Id != id && s.UserId == userId))
        {
            return CatalogServiceResult.ConflictResult("Ya existe un estado con este nombre");
        }

        if (updateDto.IsDefault.HasValue && updateDto.IsDefault.Value && !gameStatus.IsDefault)
        {
            var targetStatusType = gameStatus.StatusType != SpecialStatusType.None
                ? gameStatus.StatusType
                : SpecialStatusType.NotFulfilled;

            var currentDefault = await _context.GameStatuses
                .FirstOrDefaultAsync(s => s.IsDefault && s.StatusType == targetStatusType && s.UserId == userId);

            if (currentDefault != null)
            {
                currentDefault.IsDefault = false;
                currentDefault.StatusType = SpecialStatusType.None;
                await _context.SaveChangesAsync();
            }

            gameStatus.IsDefault = true;
            gameStatus.StatusType = targetStatusType;
        }

        gameStatus.Name = updateDto.Name;
        gameStatus.IsActive = updateDto.IsActive;
        gameStatus.Color = updateDto.Color;
        if (updateDto.SortOrder.HasValue)
        {
            gameStatus.SortOrder = updateDto.SortOrder.Value;
        }

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!_context.GameStatuses.Any(e => e.Id == id && e.UserId == userId))
                return CatalogServiceResult.NotFoundResult("Estado no encontrado");
            else
                return CatalogServiceResult.ConflictResult("Conflicto de concurrencia");
        }

        return CatalogServiceResult.Ok();
    }

    public async Task<CatalogServiceResult> ReorderStatusesAsync(ReorderStatusesDto dto, int userId)
    {
        if (dto?.OrderedIds == null || dto.OrderedIds.Count == 0)
            return CatalogServiceResult.BadRequest("OrderedIds must be provided");

        var statuses = await _context.GameStatuses
            .Where(s => dto.OrderedIds.Contains(s.Id) && s.UserId == userId)
            .ToListAsync();

        if (statuses.Count != dto.OrderedIds.Count)
        {
            return CatalogServiceResult.NotFoundResult("One or more status IDs not found");
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            for (int i = 0; i < dto.OrderedIds.Count; i++)
            {
                var id = dto.OrderedIds[i];
                var status = statuses.First(s => s.Id == id);
                status.SortOrder = i + 1;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return CatalogServiceResult.Ok();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return CatalogServiceResult.ServerError("Error reordering statuses");
        }
    }

    public async Task<CatalogServiceResult> DeleteStatusAsync(int id, int userId)
    {
        var gameStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (gameStatus == null)
            return CatalogServiceResult.NotFoundResult("Estado no encontrado");

        if (gameStatus.IsSpecialStatus && gameStatus.IsDefault)
        {
            return CatalogServiceResult.BadRequest("No se puede eliminar un estado especial activo", new
            {
                message = "No se puede eliminar un estado especial activo",
                details = $"El estado '{gameStatus.Name}' es actualmente un estado especial activo de tipo {gameStatus.StatusType}",
                statusType = gameStatus.StatusType.ToString(),
                isDefault = gameStatus.IsDefault
            });
        }

        var gamesUsingStatus = await _context.Games.CountAsync(g => g.StatusId == id && g.UserId == userId);
        if (gamesUsingStatus > 0)
        {
            return CatalogServiceResult.BadRequest("No se puede eliminar el estado", new
            {
                message = "No se puede eliminar el estado",
                details = $"Hay {gamesUsingStatus} juego(s) que usan este estado",
                gamesCount = gamesUsingStatus
            });
        }

        _context.GameStatuses.Remove(gameStatus);
        await _context.SaveChangesAsync();

        return CatalogServiceResult.Ok();
    }

    public async Task<IEnumerable<SpecialStatusDto>> GetSpecialStatusesAsync(int userId)
    {
        var specialStatuses = await _context.GameStatuses
            .Where(s => s.StatusType != SpecialStatusType.None && s.IsDefault && s.UserId == userId)
            .OrderBy(s => s.StatusType)
            .ThenBy(s => s.Name)
            .ToListAsync();

        return specialStatuses.Select(s => s.ToSpecialStatusDto());
    }

    public async Task<CatalogServiceResult<object>> ReassignSpecialStatusAsync(ReassignDefaultStatusDto reassignDto, int userId)
    {
        if (!Enum.TryParse<SpecialStatusType>(reassignDto.StatusType, out var statusType) ||
            statusType == SpecialStatusType.None)
        {
            return CatalogServiceResult<object>.BadRequest("Tipo de estado especial inválido");
        }

        var newDefaultStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.Id == reassignDto.NewDefaultStatusId && s.UserId == userId);

        if (newDefaultStatus == null)
            return CatalogServiceResult<object>.NotFoundResult("Estado no encontrado");

        var currentDefaultStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.IsDefault && s.StatusType == statusType && s.UserId == userId);

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            if (currentDefaultStatus != null)
            {
                currentDefaultStatus.IsDefault = false;
                currentDefaultStatus.StatusType = SpecialStatusType.None;
                await _context.SaveChangesAsync();
            }

            newDefaultStatus.IsDefault = true;
            newDefaultStatus.StatusType = statusType;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return CatalogServiceResult<object>.Ok(new
            {
                message = "Estado especial reasignado exitosamente",
                previousDefault = currentDefaultStatus?.Name,
                newDefault = newDefaultStatus.Name,
                statusType = statusType.ToString()
            });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return CatalogServiceResult<object>.ServerError("Error al reasignar el estado especial");
        }
    }

    public async Task<CatalogServiceResult<object>> DeleteSpecialStatusAsync(int id, int userId)
    {
        var gameStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (gameStatus == null)
            return CatalogServiceResult<object>.NotFoundResult("Estado no encontrado");

        if (gameStatus.IsDefault)
        {
            return CatalogServiceResult<object>.BadRequest("No se puede eliminar el estado por defecto", new
            {
                message = "No se puede eliminar el estado por defecto",
                statusType = gameStatus.StatusType.ToString()
            });
        }

        var gamesUsingStatus = await _context.Games.CountAsync(g => g.StatusId == id && g.UserId == userId);
        if (gamesUsingStatus > 0)
        {
            return CatalogServiceResult<object>.BadRequest("No se puede eliminar el estado", new
            {
                message = "No se puede eliminar el estado",
                gamesCount = gamesUsingStatus
            });
        }

        _context.GameStatuses.Remove(gameStatus);
        await _context.SaveChangesAsync();

        return CatalogServiceResult<object>.Ok(new
        {
            message = "Estado especial eliminado exitosamente",
            statusType = gameStatus.StatusType.ToString()
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PLATFORMS
    // ═══════════════════════════════════════════════════════════════════════════

    private const int MaxLogoLength = 500_000;

    public async Task<PagedResult<GamePlatformDto>> GetPlatformsAsync(QueryParameters parameters, int userId)
    {
        var query = _context.GamePlatforms.Where(p => p.UserId == userId).AsQueryable();
        if (!string.IsNullOrEmpty(parameters.Search))
        {
            query = query.Where(p => p.Name.Contains(parameters.Search));
        }

        if (parameters.IsActive.HasValue)
        {
            query = query.Where(p => p.IsActive == parameters.IsActive.Value);
        }

        if (!string.IsNullOrEmpty(parameters.SortBy))
        {
            query = parameters.SortBy.ToLower() switch
            {
                "name" or "alphabetical" => parameters.SortDescending ?
                    query.OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE")).Reverse() :
                    query.OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE")),
                "isactive" => parameters.SortDescending ? query.OrderByDescending(p => p.IsActive) : query.OrderBy(p => p.IsActive),
                "creation" or "id" => parameters.SortDescending ? query.OrderByDescending(p => p.Id) : query.OrderBy(p => p.Id),
                "sortorder" or "order" or "position" => parameters.SortDescending ? query.OrderByDescending(p => p.SortOrder) : query.OrderBy(p => p.SortOrder),
                _ => query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE"))
            };
        }
        else
        {
            query = query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE"));
        }

        var totalCount = await query.CountAsync();

        var platforms = await query
            .Skip(parameters.Skip)
            .Take(parameters.Take)
            .ToListAsync();

        var platformDtos = platforms.Select(p => p.ToDto()).ToList();

        return new PagedResult<GamePlatformDto>
        {
            Data = platformDtos,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        };
    }

    public async Task<IEnumerable<GamePlatformDto>> GetActivePlatformsAsync(int userId)
    {
        var platforms = await _context.GamePlatforms
            .Where(p => p.IsActive && p.UserId == userId)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE"))
            .ToListAsync();

        return platforms.Select(p => p.ToDto());
    }

    public async Task<GamePlatformDto?> GetPlatformByIdAsync(int id, int userId)
    {
        var gamePlatform = await _context.GamePlatforms
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        return gamePlatform?.ToDto();
    }

    public async Task<CatalogServiceResult<GamePlatformDto>> CreatePlatformAsync(GamePlatformCreateDto createDto, int userId)
    {
        if (await _context.GamePlatforms.AnyAsync(p => p.Name.ToLower() == createDto.Name.ToLower() && p.UserId == userId))
        {
            return CatalogServiceResult<GamePlatformDto>.ConflictResult("Ya existe una plataforma con este nombre");
        }

        var logoValidation = ValidatePlatformLogo(createDto.Logo);
        if (logoValidation != null)
        {
            return CatalogServiceResult<GamePlatformDto>.BadRequest(logoValidation);
        }

        var maxSort = await _context.GamePlatforms
            .Where(p => p.UserId == userId)
            .MaxAsync(p => (int?)p.SortOrder) ?? 0;

        var gamePlatform = new GamePlatform
        {
            UserId = userId,
            Name = createDto.Name,
            SortOrder = maxSort + 1,
            IsActive = createDto.IsActive,
            Color = createDto.Color,
            Logo = NormalizeLogo(createDto.Logo)
        };

        _context.GamePlatforms.Add(gamePlatform);
        await _context.SaveChangesAsync();

        return CatalogServiceResult<GamePlatformDto>.Ok(gamePlatform.ToDto());
    }

    public async Task<CatalogServiceResult> UpdatePlatformAsync(int id, GamePlatformUpdateDto updateDto, int userId)
    {
        var gamePlatform = await _context.GamePlatforms
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (gamePlatform == null)
            return CatalogServiceResult.NotFoundResult("Plataforma no encontrada");

        if (await _context.GamePlatforms.AnyAsync(p => p.Name.ToLower() == updateDto.Name.ToLower() && p.Id != id && p.UserId == userId))
        {
            return CatalogServiceResult.ConflictResult("Ya existe una plataforma con este nombre");
        }

        var logoValidation = ValidatePlatformLogo(updateDto.Logo);
        if (logoValidation != null)
        {
            return CatalogServiceResult.BadRequest(logoValidation);
        }

        gamePlatform.Name = updateDto.Name;
        gamePlatform.IsActive = updateDto.IsActive;
        gamePlatform.Color = updateDto.Color;
        gamePlatform.Logo = NormalizeLogo(updateDto.Logo);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!_context.GamePlatforms.Any(e => e.Id == id && e.UserId == userId))
                return CatalogServiceResult.NotFoundResult("Plataforma no encontrada");
            else
                return CatalogServiceResult.ConflictResult("Conflicto de concurrencia");
        }

        return CatalogServiceResult.Ok();
    }

    public async Task<CatalogServiceResult> ReorderPlatformsAsync(ReorderStatusesDto dto, int userId)
    {
        if (dto?.OrderedIds == null || dto.OrderedIds.Count == 0)
            return CatalogServiceResult.BadRequest("OrderedIds must be provided");

        var platforms = await _context.GamePlatforms
            .Where(p => dto.OrderedIds.Contains(p.Id) && p.UserId == userId)
            .ToListAsync();

        if (platforms.Count != dto.OrderedIds.Count)
        {
            return CatalogServiceResult.NotFoundResult("One or more platform IDs not found");
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            for (int i = 0; i < dto.OrderedIds.Count; i++)
            {
                var id = dto.OrderedIds[i];
                var platform = platforms.First(p => p.Id == id);
                platform.SortOrder = i + 1;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return CatalogServiceResult.Ok();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return CatalogServiceResult.ServerError("Error reordering platforms");
        }
    }

    public async Task<CatalogServiceResult> DeletePlatformAsync(int id, int userId)
    {
        var gamePlatform = await _context.GamePlatforms
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (gamePlatform == null)
            return CatalogServiceResult.NotFoundResult("Plataforma no encontrada");

        var gamesUsingPlatform = await _context.Games.CountAsync(g => g.PlatformId == id && g.UserId == userId);
        if (gamesUsingPlatform > 0)
        {
            return CatalogServiceResult.BadRequest("No se puede eliminar la plataforma", new
            {
                message = "No se puede eliminar la plataforma",
                details = $"Hay {gamesUsingPlatform} juego(s) que usan esta plataforma",
                gamesCount = gamesUsingPlatform
            });
        }

        _context.GamePlatforms.Remove(gamePlatform);
        await _context.SaveChangesAsync();

        return CatalogServiceResult.Ok();
    }

    private static string? NormalizeLogo(string? logo)
    {
        return string.IsNullOrWhiteSpace(logo) ? null : logo.Trim();
    }

    private static string? ValidatePlatformLogo(string? logo)
    {
        var normalized = NormalizeLogo(logo);
        if (normalized == null)
        {
            return null;
        }

        if (normalized.Length > MaxLogoLength)
        {
            return "El logo es demasiado grande";
        }

        if (IsValidImageUrl(normalized) || IsValidImageDataUrl(normalized))
        {
            return null;
        }

        return "El logo debe ser una URL http(s) o una imagen PNG, JPG, WebP o SVG en data URL";
    }

    private static bool IsValidImageUrl(string logo)
    {
        return Uri.TryCreate(logo, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static bool IsValidImageDataUrl(string logo)
    {
        var allowedPrefixes = new[]
        {
            "data:image/png;base64,",
            "data:image/jpeg;base64,",
            "data:image/jpg;base64,",
            "data:image/webp;base64,",
            "data:image/avif;base64,",
            "data:image/svg+xml,"
        };

        return allowedPrefixes.Any(prefix => logo.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PLAYWITH
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<PagedResult<GamePlayWithDto>> GetPlayWithsAsync(QueryParameters parameters, int userId)
    {
        var query = _context.GamePlayWiths.Where(p => p.UserId == userId).AsQueryable();

        if (!string.IsNullOrEmpty(parameters.Search))
        {
            query = query.Where(p => p.Name.Contains(parameters.Search));
        }

        if (parameters.IsActive.HasValue)
        {
            query = query.Where(p => p.IsActive == parameters.IsActive.Value);
        }

        if (!string.IsNullOrEmpty(parameters.SortBy))
        {
            query = parameters.SortBy.ToLower() switch
            {
                "name" or "alphabetical" => parameters.SortDescending ?
                    query.OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE")).Reverse() :
                    query.OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE")),
                "isactive" => parameters.SortDescending ? query.OrderByDescending(p => p.IsActive) : query.OrderBy(p => p.IsActive),
                "creation" or "id" => parameters.SortDescending ? query.OrderByDescending(p => p.Id) : query.OrderBy(p => p.Id),
                _ => query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE"))
            };
        }
        else
        {
            query = query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE"));
        }

        var totalCount = await query.CountAsync();
        var items = await query.Skip(parameters.Skip).Take(parameters.Take).ToListAsync();

        return new PagedResult<GamePlayWithDto>
        {
            Data = items.Select(p => p.ToDto()),
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        };
    }

    public async Task<IEnumerable<GamePlayWithDto>> GetActivePlayWithsAsync(int userId)
    {
        var items = await _context.GamePlayWiths
            .Where(p => p.IsActive && p.UserId == userId)
            .OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE"))
            .ToListAsync();

        return items.Select(p => p.ToDto());
    }

    public async Task<GamePlayWithDto?> GetPlayWithByIdAsync(int id, int userId)
    {
        var item = await _context.GamePlayWiths
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        return item?.ToDto();
    }

    public async Task<CatalogServiceResult<GamePlayWithDto>> CreatePlayWithAsync(GamePlayWithCreateDto createDto, int userId)
    {
        if (await _context.GamePlayWiths.AnyAsync(p => p.Name.ToLower() == createDto.Name.ToLower() && p.UserId == userId))
        {
            return CatalogServiceResult<GamePlayWithDto>.ConflictResult("Ya existe un elemento con este nombre");
        }

        var maxSort = await _context.GamePlayWiths
            .Where(p => p.UserId == userId)
            .MaxAsync(p => (int?)p.SortOrder) ?? 0;

        var item = new GamePlayWith
        {
            UserId = userId,
            Name = createDto.Name,
            SortOrder = maxSort + 1,
            IsActive = createDto.IsActive,
            Color = createDto.Color
        };

        _context.GamePlayWiths.Add(item);
        await _context.SaveChangesAsync();

        return CatalogServiceResult<GamePlayWithDto>.Ok(item.ToDto());
    }

    public async Task<CatalogServiceResult> UpdatePlayWithAsync(int id, GamePlayWithUpdateDto updateDto, int userId)
    {
        var item = await _context.GamePlayWiths
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (item == null)
            return CatalogServiceResult.NotFoundResult("Elemento no encontrado");

        if (await _context.GamePlayWiths.AnyAsync(p => p.Name.ToLower() == updateDto.Name.ToLower() && p.Id != id && p.UserId == userId))
        {
            return CatalogServiceResult.ConflictResult("Ya existe un elemento con este nombre");
        }

        item.Name = updateDto.Name;
        item.IsActive = updateDto.IsActive;
        item.Color = updateDto.Color;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!_context.GamePlayWiths.Any(e => e.Id == id && e.UserId == userId))
                return CatalogServiceResult.NotFoundResult("Elemento no encontrado");
            else
                return CatalogServiceResult.ConflictResult("Conflicto de concurrencia");
        }

        return CatalogServiceResult.Ok();
    }

    public async Task<CatalogServiceResult> ReorderPlayWithsAsync(ReorderStatusesDto dto, int userId)
    {
        if (dto?.OrderedIds == null || dto.OrderedIds.Count == 0)
            return CatalogServiceResult.BadRequest("OrderedIds must be provided");

        var items = await _context.GamePlayWiths
            .Where(p => dto.OrderedIds.Contains(p.Id) && p.UserId == userId)
            .ToListAsync();

        if (items.Count != dto.OrderedIds.Count)
        {
            return CatalogServiceResult.NotFoundResult("One or more play-with IDs not found");
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            for (int i = 0; i < dto.OrderedIds.Count; i++)
            {
                var id = dto.OrderedIds[i];
                var item = items.First(p => p.Id == id);
                item.SortOrder = i + 1;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return CatalogServiceResult.Ok();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return CatalogServiceResult.ServerError("Error reordering play-with options");
        }
    }

    public async Task<CatalogServiceResult> DeletePlayWithAsync(int id, int userId)
    {
        var item = await _context.GamePlayWiths
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (item == null)
            return CatalogServiceResult.NotFoundResult("Elemento no encontrado");

        var gamesUsingPlayWith = await _context.GamePlayWithMappings
            .Where(m => m.PlayWithId == id)
            .Join(_context.Games, m => m.GameId, g => g.Id, (m, g) => g)
            .CountAsync(g => g.UserId == userId);

        if (gamesUsingPlayWith > 0)
        {
            return CatalogServiceResult.BadRequest("No se puede eliminar el elemento", new
            {
                message = "No se puede eliminar el elemento",
                details = $"Hay {gamesUsingPlayWith} juego(s) que usan este elemento",
                gamesCount = gamesUsingPlayWith
            });
        }

        _context.GamePlayWiths.Remove(item);
        await _context.SaveChangesAsync();

        return CatalogServiceResult.Ok();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PLAYED STATUSES
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<PagedResult<GamePlayedStatusDto>> GetPlayedStatusesAsync(QueryParameters parameters, int userId)
    {
        var query = _context.GamePlayedStatuses.Where(p => p.UserId == userId).AsQueryable();

        if (!string.IsNullOrEmpty(parameters.Search))
        {
            query = query.Where(p => p.Name.Contains(parameters.Search));
        }

        if (parameters.IsActive.HasValue)
        {
            query = query.Where(p => p.IsActive == parameters.IsActive.Value);
        }

        if (!string.IsNullOrEmpty(parameters.SortBy))
        {
            query = parameters.SortBy.ToLower() switch
            {
                "name" or "alphabetical" => parameters.SortDescending ?
                    query.OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE")).Reverse() :
                    query.OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE")),
                "isactive" => parameters.SortDescending ? query.OrderByDescending(p => p.IsActive) : query.OrderBy(p => p.IsActive),
                "creation" or "id" => parameters.SortDescending ? query.OrderByDescending(p => p.Id) : query.OrderBy(p => p.Id),
                _ => query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE"))
            };
        }
        else
        {
            query = query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE"));
        }

        var totalCount = await query.CountAsync();
        var items = await query.Skip(parameters.Skip).Take(parameters.Take).ToListAsync();

        return new PagedResult<GamePlayedStatusDto>
        {
            Data = items.Select(p => p.ToDto()),
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        };
    }

    public async Task<IEnumerable<GamePlayedStatusDto>> GetActivePlayedStatusesAsync(int userId)
    {
        var items = await _context.GamePlayedStatuses
            .Where(p => p.IsActive && p.UserId == userId)
            .OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE"))
            .ToListAsync();

        return items.Select(p => p.ToDto());
    }

    public async Task<GamePlayedStatusDto?> GetPlayedStatusByIdAsync(int id, int userId)
    {
        var item = await _context.GamePlayedStatuses
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        return item?.ToDto();
    }

    public async Task<CatalogServiceResult<GamePlayedStatusDto>> CreatePlayedStatusAsync(GamePlayedStatusCreateDto createDto, int userId)
    {
        if (await _context.GamePlayedStatuses.AnyAsync(p => p.Name.ToLower() == createDto.Name.ToLower() && p.UserId == userId))
        {
            return CatalogServiceResult<GamePlayedStatusDto>.ConflictResult("Ya existe un estado con este nombre");
        }

        var maxSort = await _context.GamePlayedStatuses
            .Where(p => p.UserId == userId)
            .MaxAsync(p => (int?)p.SortOrder) ?? 0;

        var item = new GamePlayedStatus
        {
            UserId = userId,
            Name = createDto.Name,
            SortOrder = maxSort + 1,
            IsActive = createDto.IsActive,
            Color = createDto.Color
        };

        _context.GamePlayedStatuses.Add(item);
        await _context.SaveChangesAsync();

        return CatalogServiceResult<GamePlayedStatusDto>.Ok(item.ToDto());
    }

    public async Task<CatalogServiceResult> UpdatePlayedStatusAsync(int id, GamePlayedStatusUpdateDto updateDto, int userId)
    {
        var item = await _context.GamePlayedStatuses
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (item == null)
            return CatalogServiceResult.NotFoundResult("Estado no encontrado");

        if (await _context.GamePlayedStatuses.AnyAsync(p => p.Name.ToLower() == updateDto.Name.ToLower() && p.Id != id && p.UserId == userId))
        {
            return CatalogServiceResult.ConflictResult("Ya existe un estado con este nombre");
        }

        item.Name = updateDto.Name;
        item.IsActive = updateDto.IsActive;
        item.Color = updateDto.Color;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!_context.GamePlayedStatuses.Any(e => e.Id == id && e.UserId == userId))
                return CatalogServiceResult.NotFoundResult("Estado no encontrado");
            else
                return CatalogServiceResult.ConflictResult("Conflicto de concurrencia");
        }

        return CatalogServiceResult.Ok();
    }

    public async Task<CatalogServiceResult> ReorderPlayedStatusesAsync(ReorderStatusesDto dto, int userId)
    {
        if (dto?.OrderedIds == null || dto.OrderedIds.Count == 0)
            return CatalogServiceResult.BadRequest("OrderedIds must be provided");

        var statuses = await _context.GamePlayedStatuses
            .Where(s => dto.OrderedIds.Contains(s.Id) && s.UserId == userId)
            .ToListAsync();

        if (statuses.Count != dto.OrderedIds.Count)
        {
            return CatalogServiceResult.NotFoundResult("One or more played status IDs not found");
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            for (int i = 0; i < dto.OrderedIds.Count; i++)
            {
                var id = dto.OrderedIds[i];
                var status = statuses.First(s => s.Id == id);
                status.SortOrder = i + 1;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return CatalogServiceResult.Ok();
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return CatalogServiceResult.ServerError("Error reordering played statuses");
        }
    }

    public async Task<CatalogServiceResult> DeletePlayedStatusAsync(int id, int userId)
    {
        var item = await _context.GamePlayedStatuses
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (item == null)
            return CatalogServiceResult.NotFoundResult("Estado no encontrado");

        var gamesUsingPlayedStatus = await _context.Games.CountAsync(g => g.PlayedStatusId == id && g.UserId == userId);
        if (gamesUsingPlayedStatus > 0)
        {
            return CatalogServiceResult.BadRequest("No se puede eliminar el estado", new
            {
                message = "No se puede eliminar el estado",
                details = $"Hay {gamesUsingPlayedStatus} juego(s) que usan este estado",
                gamesCount = gamesUsingPlayedStatus
            });
        }

        _context.GamePlayedStatuses.Remove(item);
        await _context.SaveChangesAsync();

        return CatalogServiceResult.Ok();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // REPLAY TYPES
    // ═══════════════════════════════════════════════════════════════════════════

    public async Task<PagedResult<GameReplayTypeDto>> GetReplayTypesAsync(QueryParameters parameters, int userId)
    {
        var query = _context.GameReplayTypes.Where(t => t.UserId == userId).AsQueryable();

        if (!string.IsNullOrEmpty(parameters.Search))
            query = query.Where(t => t.Name.Contains(parameters.Search));

        if (parameters.IsActive.HasValue)
            query = query.Where(t => t.IsActive == parameters.IsActive.Value);

        query = parameters.SortBy?.ToLower() switch
        {
            "name" or "alphabetical" => parameters.SortDescending
                ? query.OrderByDescending(t => EF.Functions.Collate(t.Name, "NOCASE"))
                : query.OrderBy(t => EF.Functions.Collate(t.Name, "NOCASE")),
            "creation" or "id" => parameters.SortDescending
                ? query.OrderByDescending(t => t.Id)
                : query.OrderBy(t => t.Id),
            _ => query.OrderBy(t => t.SortOrder).ThenBy(t => EF.Functions.Collate(t.Name, "NOCASE"))
        };

        var totalCount = await query.CountAsync();
        var items = await query.Skip(parameters.Skip).Take(parameters.Take).ToListAsync();

        return new PagedResult<GameReplayTypeDto>
        {
            Data = items.Select(t => t.ToDto()),
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        };
    }

    public async Task<IEnumerable<GameReplayTypeDto>> GetActiveReplayTypesAsync(int userId)
    {
        var items = await _context.GameReplayTypes
            .Where(t => t.IsActive && t.UserId == userId)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => EF.Functions.Collate(t.Name, "NOCASE"))
            .ToListAsync();

        return items.Select(t => t.ToDto());
    }

    public async Task<GameReplayTypeDto?> GetReplayTypeByIdAsync(int id, int userId)
    {
        var item = await _context.GameReplayTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        return item?.ToDto();
    }

    public async Task<CatalogServiceResult<GameReplayTypeDto>> GetOrCreateSpecialReplayTypeAsync(int userId)
    {
        var item = await _context.GameReplayTypes
            .FirstOrDefaultAsync(t => t.ReplayType == SpecialReplayType.Replay && t.UserId == userId);

        if (item == null)
        {
            item = new GameReplayType
            {
                Name = "Rejugado",
                Color = "#61afef",
                SortOrder = 1,
                IsDefault = true,
                ReplayType = SpecialReplayType.Replay,
                UserId = userId
            };
            _context.GameReplayTypes.Add(item);
            await _context.SaveChangesAsync();
        }

        return CatalogServiceResult<GameReplayTypeDto>.Ok(item.ToDto());
    }

    public async Task<CatalogServiceResult<GameReplayTypeDto>> CreateReplayTypeAsync(GameReplayTypeCreateDto createDto, int userId)
    {
        if (await _context.GameReplayTypes.AnyAsync(t => t.Name.ToLower() == createDto.Name.ToLower() && t.UserId == userId))
            return CatalogServiceResult<GameReplayTypeDto>.ConflictResult("Ya existe un tipo con este nombre");

        var maxSortOrder = await _context.GameReplayTypes
            .Where(t => t.UserId == userId)
            .Select(t => (int?)t.SortOrder)
            .MaxAsync() ?? 0;

        var item = new GameReplayType
        {
            Name = createDto.Name,
            IsActive = createDto.IsActive,
            Color = createDto.Color,
            SortOrder = createDto.SortOrder ?? maxSortOrder + 1,
            UserId = userId
        };

        _context.GameReplayTypes.Add(item);
        await _context.SaveChangesAsync();

        return CatalogServiceResult<GameReplayTypeDto>.Ok(item.ToDto());
    }

    public async Task<CatalogServiceResult> UpdateReplayTypeAsync(int id, GameReplayTypeUpdateDto updateDto, int userId)
    {
        var item = await _context.GameReplayTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (item == null)
            return CatalogServiceResult.NotFoundResult("Tipo no encontrado");

        if (await _context.GameReplayTypes.AnyAsync(t => t.Name.ToLower() == updateDto.Name.ToLower() && t.Id != id && t.UserId == userId))
            return CatalogServiceResult.ConflictResult("Ya existe un tipo con este nombre");

        item.Name = updateDto.Name;
        item.IsActive = updateDto.IsActive;
        item.Color = updateDto.Color;
        if (updateDto.SortOrder.HasValue) item.SortOrder = updateDto.SortOrder.Value;

        await _context.SaveChangesAsync();
        return CatalogServiceResult.Ok();
    }

    public async Task<CatalogServiceResult> ReorderReplayTypesAsync(ReorderReplayTypesDto reorderDto, int userId)
    {
        var types = await _context.GameReplayTypes
            .Where(t => t.UserId == userId && reorderDto.OrderedIds.Contains(t.Id))
            .ToListAsync();

        for (int i = 0; i < reorderDto.OrderedIds.Count; i++)
        {
            var type = types.FirstOrDefault(t => t.Id == reorderDto.OrderedIds[i]);
            if (type != null) type.SortOrder = i + 1;
        }

        await _context.SaveChangesAsync();
        return CatalogServiceResult.Ok();
    }

    public async Task<CatalogServiceResult> DeleteReplayTypeAsync(int id, int userId)
    {
        var item = await _context.GameReplayTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (item == null)
            return CatalogServiceResult.NotFoundResult("Tipo no encontrado");

        if (item.IsSpecialType)
            return CatalogServiceResult.ConflictResult("No se puede eliminar el tipo especial de rejugada");

        var specialType = await _context.GameReplayTypes
            .FirstOrDefaultAsync(t => t.UserId == userId && t.ReplayType == SpecialReplayType.Replay);

        if (specialType != null)
        {
            var affected = await _context.GameReplays
                .Where(r => r.ReplayTypeId == id && r.UserId == userId)
                .ToListAsync();
            foreach (var replay in affected)
                replay.ReplayTypeId = specialType.Id;
        }

        _context.GameReplayTypes.Remove(item);
        await _context.SaveChangesAsync();

        return CatalogServiceResult.Ok();
    }
}
