using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameStatusController : BaseApiController
{
    private readonly GamesDbContext _context;

    public GameStatusController(GamesDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GameStatusDto>>> GetGameStatuses([FromQuery] QueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var query = _context.GameStatuses.Where(s => s.UserId == userId).AsQueryable();

        // Aplicar filtros
        if (!string.IsNullOrEmpty(parameters.Search))
        {
            query = query.Where(s => s.Name.Contains(parameters.Search));
        }

        if (parameters.IsActive.HasValue)
        {
            query = query.Where(s => s.IsActive == parameters.IsActive.Value);
        }

        // Aplicar ordenado
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
                _ => query.OrderBy(s => s.SortOrder).ThenBy(s => EF.Functions.Collate(s.Name, "NOCASE")) // Default: use SortOrder then name
            };
        }
        else
        {
            query = query.OrderBy(s => s.SortOrder).ThenBy(s => EF.Functions.Collate(s.Name, "NOCASE")); // Default: use SortOrder then name
        }

        var totalCount = await query.CountAsync();

        var statuses = await query
            .Skip(parameters.Skip)
            .Take(parameters.Take)
            .ToListAsync();

        var statusDtos = statuses.Select(s => s.ToDto()).ToList();

        return Ok(new PagedResult<GameStatusDto>
        {
            Data = statusDtos,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<GameStatusDto>>> GetActiveGameStatuses()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var statuses = await _context.GameStatuses
            .Where(s => s.IsActive && s.UserId == userId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => EF.Functions.Collate(s.Name, "NOCASE"))
            .ToListAsync();

        return Ok(statuses.Select(s => s.ToDto()));
    }

    [HttpGet("ordered")]
    public async Task<ActionResult<IEnumerable<object>>> GetOrderedStatuses()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var statuses = await _context.GameStatuses
            .Where(s => s.UserId == userId)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => EF.Functions.Collate(s.Name, "NOCASE"))
            .Select(s => new { s.Id, s.Name, s.SortOrder })
            .ToListAsync();

        return Ok(statuses);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GameStatusDto>> GetGameStatus(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var gameStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (gameStatus == null)
            return NotFound();

        return Ok(gameStatus.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGameStatus(int id, GameStatusUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var gameStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (gameStatus == null)
            return NotFound(new { message = "Estado no encontrado" });

        if (await _context.GameStatuses.AnyAsync(s => s.Name.ToLower() == updateDto.Name.ToLower() && s.Id != id && s.UserId == userId))
        {
            return Conflict(new { message = "Ya existe un estado con este nombre" });
        }

        if (updateDto.IsDefault.HasValue && updateDto.IsDefault.Value && !gameStatus.IsDefault)
        {
            var targetStatusType = gameStatus.StatusType != Models.SpecialStatusType.None
                ? gameStatus.StatusType
                : Models.SpecialStatusType.NotFulfilled;

            var currentDefault = await _context.GameStatuses
                .FirstOrDefaultAsync(s => s.IsDefault && s.StatusType == targetStatusType && s.UserId == userId);

            if (currentDefault != null)
            {
                currentDefault.IsDefault = false;
                currentDefault.StatusType = Models.SpecialStatusType.None;
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
            if (!GameStatusExists(id))
                return NotFound(new { message = "Estado no encontrado" });
            else
                return Conflict(new { message = "Conflicto de concurrencia" });
        }

        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<GameStatusDto>> PostGameStatus(GameStatusCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        if (await _context.GameStatuses.AnyAsync(s => s.Name.ToLower() == createDto.Name.ToLower() && s.UserId == userId))
        {
            return Conflict(new { message = "Ya existe un estado con este nombre" });
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

        var result = gameStatus.ToDto();
        return CreatedAtAction("GetGameStatus", new { id = gameStatus.Id }, result);
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderStatuses([FromBody] ReorderStatusesDto dto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        if (dto?.OrderedIds == null || dto.OrderedIds.Count == 0)
            return BadRequest(new { message = "OrderedIds must be provided" });

        var statuses = await _context.GameStatuses
            .Where(s => dto.OrderedIds.Contains(s.Id) && s.UserId == userId)
            .ToListAsync();

        if (statuses.Count != dto.OrderedIds.Count)
        {
            return NotFound(new { message = "One or more status IDs not found" });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            for (int i = 0; i < dto.OrderedIds.Count; i++)
            {
                var id = dto.OrderedIds[i];
                var status = statuses.First(s => s.Id == id);
                status.SortOrder = i + 1; // 1-based ordering
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { message = "Statuses reordered successfully" });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { message = "Error reordering statuses" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGameStatus(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var gameStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (gameStatus == null)
            return NotFound(new { message = "Estado no encontrado" });

        if (gameStatus.IsSpecialStatus && gameStatus.IsDefault)
        {
            return BadRequest(new
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
            return BadRequest(new
            {
                message = "No se puede eliminar el estado",
                details = $"Hay {gamesUsingStatus} juego(s) que usan este estado",
                gamesCount = gamesUsingStatus
            });
        }

        _context.GameStatuses.Remove(gameStatus);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpGet("special")]
    public async Task<ActionResult<IEnumerable<SpecialStatusDto>>> GetSpecialStatuses()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var specialStatuses = await _context.GameStatuses
            .Where(s => s.StatusType != Models.SpecialStatusType.None && s.IsDefault && s.UserId == userId)
            .OrderBy(s => s.StatusType)
            .ThenBy(s => s.Name)
            .ToListAsync();

        return Ok(specialStatuses.Select(s => s.ToSpecialStatusDto()));
    }

    [HttpPost("reassign-special")]
    public async Task<IActionResult> ReassignSpecialStatus(ReassignDefaultStatusDto reassignDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        if (!Enum.TryParse<Models.SpecialStatusType>(reassignDto.StatusType, out var statusType) ||
            statusType == Models.SpecialStatusType.None)
        {
            return BadRequest(new { message = "Tipo de estado especial inválido" });
        }

        var newDefaultStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.Id == reassignDto.NewDefaultStatusId && s.UserId == userId);

        if (newDefaultStatus == null)
            return NotFound(new { message = "Estado no encontrado" });

        var currentDefaultStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.IsDefault && s.StatusType == statusType && s.UserId == userId);

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // First, remove default flag and special status from current default status
            if (currentDefaultStatus != null)
            {
                currentDefaultStatus.IsDefault = false;
                currentDefaultStatus.StatusType = Models.SpecialStatusType.None; // Remove special status
                // Save this change first to avoid unique constraint violation
                await _context.SaveChangesAsync();
            }

            // Now set the new default status
            newDefaultStatus.IsDefault = true;
            newDefaultStatus.StatusType = statusType;

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new
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
            return StatusCode(500, new { message = "Error al reasignar el estado especial" });
        }
    }

    [HttpDelete("special/{id}")]
    public async Task<IActionResult> DeleteSpecialStatus(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var gameStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);

        if (gameStatus == null)
            return NotFound(new { message = "Estado no encontrado" });

        if (gameStatus.IsDefault)
        {
            return BadRequest(new
            {
                message = "No se puede eliminar el estado por defecto",
                statusType = gameStatus.StatusType.ToString()
            });
        }

        var gamesUsingStatus = await _context.Games.CountAsync(g => g.StatusId == id && g.UserId == userId);
        if (gamesUsingStatus > 0)
        {
            return BadRequest(new
            {
                message = "No se puede eliminar el estado",
                gamesCount = gamesUsingStatus
            });
        }

        _context.GameStatuses.Remove(gameStatus);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Estado especial eliminado exitosamente",
            statusType = gameStatus.StatusType.ToString()
        });
    }

    private bool GameStatusExists(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        return _context.GameStatuses.Any(e => e.Id == id && e.UserId == userId);
    }
}