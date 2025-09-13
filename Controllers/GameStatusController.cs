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
                _ => query.OrderBy(s => EF.Functions.Collate(s.Name, "NOCASE")) // Default: orden alfabético case-insensitive
            };
        }
        else
        {
            query = query.OrderBy(s => EF.Functions.Collate(s.Name, "NOCASE")); // Default: orden alfabético case-insensitive
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
            .OrderBy(s => EF.Functions.Collate(s.Name, "NOCASE")) // Orden alfabético case-insensitive para selectores
            .ToListAsync();

        return Ok(statuses.Select(s => s.ToDto()));
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

        gameStatus.Name = updateDto.Name;
        gameStatus.IsActive = updateDto.IsActive;
        gameStatus.Color = updateDto.Color;

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

        var gameStatus = new GameStatus
        {
            Name = createDto.Name,
            SortOrder = 0, // Ya no usamos sortOrder para ordenación
            IsActive = createDto.IsActive,
            Color = createDto.Color
        };

        _context.GameStatuses.Add(gameStatus);
        await _context.SaveChangesAsync();

        var result = gameStatus.ToDto();
        return CreatedAtAction("GetGameStatus", new { id = gameStatus.Id }, result);
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
            return NotFound();
        }

        _context.GameStatuses.Remove(gameStatus);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool GameStatusExists(int id)
    {
        return _context.GameStatuses.Any(e => e.Id == id);
    }
}