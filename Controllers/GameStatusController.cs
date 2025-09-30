using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameStatusController : ControllerBase
{
    private readonly GamesDbContext _context;

    public GameStatusController(GamesDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Obtiene todos los estados con paginado, filtrado y ordenado
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<GameStatusDto>>> GetGameStatuses([FromQuery] QueryParameters parameters)
    {
        var query = _context.GameStatuses.AsQueryable();

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

    /// <summary>
    /// Obtiene solo los estados activos ordenados alfabéticamente
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<GameStatusDto>>> GetActiveGameStatuses()
    {
        var statuses = await _context.GameStatuses
            .Where(s => s.IsActive)
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => EF.Functions.Collate(s.Name, "NOCASE")) // Orden por SortOrder luego alfabético
            .ToListAsync();

        return Ok(statuses.Select(s => s.ToDto()));
    }

    /// <summary>
    /// Returns a minimal ordered list of active statuses (Id, Name, SortOrder) for UI selectors and verification.
    /// </summary>
    [HttpGet("ordered")]
    public async Task<ActionResult<IEnumerable<object>>> GetOrderedStatuses()
    {
        var statuses = await _context.GameStatuses
            .OrderBy(s => s.SortOrder)
            .ThenBy(s => EF.Functions.Collate(s.Name, "NOCASE"))
            .Select(s => new { s.Id, s.Name, s.SortOrder })
            .ToListAsync();

        return Ok(statuses);
    }

    /// <summary>
    /// Obtiene un estado específico por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<GameStatusDto>> GetGameStatus(int id)
    {
        var gameStatus = await _context.GameStatuses.FindAsync(id);

        if (gameStatus == null)
        {
            return NotFound();
        }

        return Ok(gameStatus.ToDto());
    }

    /// <summary>
    /// Actualiza un estado existente
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> PutGameStatus(int id, GameStatusUpdateDto updateDto)
    {
        var gameStatus = await _context.GameStatuses.FindAsync(id);
        if (gameStatus == null)
        {
            return NotFound(new
            {
                message = "Estado no encontrado",
                details = "El estado que intenta actualizar no existe."
            });
        }

        // Validar que el nombre no exista en otro estado
        if (await _context.GameStatuses.AnyAsync(s => s.Name.ToLower() == updateDto.Name.ToLower() && s.Id != id))
        {
            return Conflict(new
            {
                message = "Ya existe un estado con este nombre",
                details = "El nombre del estado debe ser único. Por favor, use un nombre diferente."
            });
        }

        // Handle special status reassignment if requested
        if (updateDto.IsDefault.HasValue && updateDto.IsDefault.Value && !gameStatus.IsDefault)
        {
            // Remove default flag from current default status of the same type
            var targetStatusType = gameStatus.StatusType != Models.SpecialStatusType.None
                ? gameStatus.StatusType
                : Models.SpecialStatusType.NotFulfilled;

            var currentDefault = await _context.GameStatuses
                .FirstOrDefaultAsync(s => s.IsDefault && s.StatusType == targetStatusType);

            if (currentDefault != null)
            {
                currentDefault.IsDefault = false;
                currentDefault.StatusType = Models.SpecialStatusType.None; // Remove special status
                // Save this change first to avoid unique constraint violation
                await _context.SaveChangesAsync();
            }

            // Set this status as the new default
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
            {
                return NotFound(new
                {
                    message = "Estado no encontrado",
                    details = "El estado fue eliminado por otro usuario."
                });
            }
            else
            {
                return Conflict(new
                {
                    message = "Conflicto de concurrencia",
                    details = "El estado fue modificado por otro usuario. Por favor, recargue e intente nuevamente."
                });
            }
        }

        return NoContent();
    }

    /// <summary>
    /// Crea un nuevo estado
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<GameStatusDto>> PostGameStatus(GameStatusCreateDto createDto)
    {
        // Validar que el nombre no exista
        if (await _context.GameStatuses.AnyAsync(s => s.Name.ToLower() == createDto.Name.ToLower()))
        {
            return Conflict(new
            {
                message = "Ya existe un estado con este nombre",
                details = "El nombre del estado debe ser único. Por favor, use un nombre diferente."
            });
        }

        // Determine default sort order: append to end if none provided
        var maxSort = await _context.GameStatuses.MaxAsync(s => (int?)s.SortOrder) ?? 0;

        var gameStatus = new GameStatus
        {
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

    /// <summary>
    /// Reorder statuses by providing an ordered list of IDs.
    /// </summary>
    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderStatuses([FromBody] ReorderStatusesDto dto)
    {
        if (dto?.OrderedIds == null || dto.OrderedIds.Count == 0)
        {
            return BadRequest(new { message = "OrderedIds must be provided" });
        }

        // Fetch statuses to ensure all IDs exist
        var statuses = await _context.GameStatuses
            .Where(s => dto.OrderedIds.Contains(s.Id))
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

    /// <summary>
    /// Elimina un estado
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGameStatus(int id)
    {
        var gameStatus = await _context.GameStatuses.FindAsync(id);
        if (gameStatus == null)
        {
            return NotFound(new
            {
                message = "Estado no encontrado",
                details = "El estado que intenta eliminar no existe."
            });
        }

        // Prevent deletion of special statuses (only those that are currently default)
        if (gameStatus.IsSpecialStatus && gameStatus.IsDefault)
        {
            return BadRequest(new
            {
                message = "No se puede eliminar un estado especial activo",
                details = $"El estado '{gameStatus.Name}' es actualmente un estado especial activo de tipo {gameStatus.StatusType} y no puede ser eliminado directamente. " +
                         "Primero debe reasignarlo a otro estado usando el endpoint de reasignación.",
                statusType = gameStatus.StatusType.ToString(),
                isDefault = gameStatus.IsDefault
            });
        }

        // Check if any games are using this status
        var gamesUsingStatus = await _context.Games.CountAsync(g => g.StatusId == id);
        if (gamesUsingStatus > 0)
        {
            return BadRequest(new
            {
                message = "No se puede eliminar el estado",
                details = $"Hay {gamesUsingStatus} juego(s) que usan este estado. " +
                         "Primero debe cambiar el estado de estos juegos antes de eliminar este estado.",
                gamesCount = gamesUsingStatus
            });
        }

        _context.GameStatuses.Remove(gameStatus);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Obtiene todos los estados especiales (que no pueden ser eliminados directamente)
    /// </summary>
    [HttpGet("special")]
    public async Task<ActionResult<IEnumerable<SpecialStatusDto>>> GetSpecialStatuses()
    {
        var specialStatuses = await _context.GameStatuses
            .Where(s => s.StatusType != Models.SpecialStatusType.None && s.IsDefault)
            .OrderBy(s => s.StatusType)
            .ThenBy(s => s.Name)
            .ToListAsync();

        return Ok(specialStatuses.Select(s => s.ToSpecialStatusDto()));
    }

    /// <summary>
    /// Reasigna un estado especial a otro estado
    /// </summary>
    [HttpPost("reassign-special")]
    public async Task<IActionResult> ReassignSpecialStatus(ReassignDefaultStatusDto reassignDto)
    {
        // Parse the status type
        if (!Enum.TryParse<Models.SpecialStatusType>(reassignDto.StatusType, out var statusType) ||
            statusType == Models.SpecialStatusType.None)
        {
            return BadRequest(new
            {
                message = "Tipo de estado especial inválido",
                details = "El tipo de estado especial debe ser uno de los valores válidos (ej: NotFulfilled)."
            });
        }

        // Find the new status to become default
        var newDefaultStatus = await _context.GameStatuses.FindAsync(reassignDto.NewDefaultStatusId);
        if (newDefaultStatus == null)
        {
            return NotFound(new
            {
                message = "Estado no encontrado",
                details = "El estado al que intenta reasignar no existe."
            });
        }

        // Find the current default status
        var currentDefaultStatus = await _context.GameStatuses
            .FirstOrDefaultAsync(s => s.IsDefault && s.StatusType == statusType);

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
                details = $"El estado '{newDefaultStatus.Name}' ahora es el estado especial por defecto de tipo {statusType}. " +
                         $"El estado anterior '{currentDefaultStatus?.Name}' ya no es especial y puede ser eliminado normalmente.",
                previousDefault = currentDefaultStatus?.Name,
                newDefault = newDefaultStatus.Name,
                statusType = statusType.ToString()
            });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new
            {
                message = "Error al reasignar el estado especial",
                details = "Ocurrió un error interno del servidor al procesar la reasignación."
            });
        }
    }

    /// <summary>
    /// Permite eliminar un estado especial después de reasignarlo
    /// </summary>
    [HttpDelete("special/{id}")]
    public async Task<IActionResult> DeleteSpecialStatus(int id)
    {
        var gameStatus = await _context.GameStatuses.FindAsync(id);
        if (gameStatus == null)
        {
            return NotFound(new
            {
                message = "Estado no encontrado",
                details = "El estado que intenta eliminar no existe."
            });
        }

        // Check if it's still a default status
        if (gameStatus.IsDefault)
        {
            return BadRequest(new
            {
                message = "No se puede eliminar el estado por defecto",
                details = $"El estado '{gameStatus.Name}' es actualmente el estado por defecto de tipo {gameStatus.StatusType}. " +
                         "Primero debe reasignar este tipo de estado especial a otro estado usando el endpoint de reasignación.",
                statusType = gameStatus.StatusType.ToString()
            });
        }

        // Check if any games are using this status
        var gamesUsingStatus = await _context.Games.CountAsync(g => g.StatusId == id);
        if (gamesUsingStatus > 0)
        {
            return BadRequest(new
            {
                message = "No se puede eliminar el estado",
                details = $"Hay {gamesUsingStatus} juego(s) que usan este estado. " +
                         "Primero debe cambiar el estado de estos juegos antes de eliminar este estado.",
                gamesCount = gamesUsingStatus
            });
        }

        _context.GameStatuses.Remove(gameStatus);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Estado especial eliminado exitosamente",
            details = $"El estado '{gameStatus.Name}' ha sido eliminado.",
            statusType = gameStatus.StatusType.ToString()
        });
    }

    private bool GameStatusExists(int id)
    {
        return _context.GameStatuses.Any(e => e.Id == id);
    }
}