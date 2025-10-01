using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamePlayedStatusController : ControllerBase
{
    private readonly GamesDbContext _context;

    public GamePlayedStatusController(GamesDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GamePlayedStatusDto>>> GetGamePlayedStatuses([FromQuery] QueryParameters parameters)
    {
        var query = _context.GamePlayedStatuses.AsQueryable();

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
                _ => query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE")) // Default: orden personalizado
            };
        }
        else
        {
            query = query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE")); // Default: orden personalizado
        }

        var totalCount = await query.CountAsync();
        var items = await query.Skip(parameters.Skip).Take(parameters.Take).ToListAsync();

        return Ok(new PagedResult<GamePlayedStatusDto>
        {
            Data = items.Select(p => p.ToDto()),
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<GamePlayedStatusDto>>> GetActiveGamePlayedStatuses()
    {
        var items = await _context.GamePlayedStatuses
            .Where(p => p.IsActive)
            .OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE")) // Orden alfabético case-insensitive
            .ToListAsync();

        return Ok(items.Select(p => p.ToDto()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GamePlayedStatusDto>> GetGamePlayedStatus(int id)
    {
        var item = await _context.GamePlayedStatuses.FindAsync(id);
        return item == null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGamePlayedStatus(int id, GamePlayedStatusUpdateDto updateDto)
    {
        var item = await _context.GamePlayedStatuses.FindAsync(id);
        if (item == null)
        {
            return NotFound(new
            {
                message = "Estado no encontrado",
                details = "El estado que intenta actualizar no existe."
            });
        }

        // Validar que el nombre no exista en otro estado
        if (await _context.GamePlayedStatuses.AnyAsync(p => p.Name.ToLower() == updateDto.Name.ToLower() && p.Id != id))
        {
            return Conflict(new
            {
                message = "Ya existe un estado con este nombre",
                details = "El nombre del estado debe ser único. Por favor, use un nombre diferente."
            });
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
            if (!_context.GamePlayedStatuses.Any(e => e.Id == id))
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

    [HttpPost]
    public async Task<ActionResult<GamePlayedStatusDto>> PostGamePlayedStatus(GamePlayedStatusCreateDto createDto)
    {
        // Validar que el nombre no exista
        if (await _context.GamePlayedStatuses.AnyAsync(p => p.Name.ToLower() == createDto.Name.ToLower()))
        {
            return Conflict(new
            {
                message = "Ya existe un estado con este nombre",
                details = "El nombre del estado debe ser único. Por favor, use un nombre diferente."
            });
        }

        var item = new GamePlayedStatus
        {
            Name = createDto.Name,
            SortOrder = 0, // Se mantiene para compatibilidad con la base de datos, pero no se usa
            IsActive = createDto.IsActive,
            Color = createDto.Color
        };

        _context.GamePlayedStatuses.Add(item);
        await _context.SaveChangesAsync();

        var result = item.ToDto();
        return CreatedAtAction("GetGamePlayedStatus", new { id = item.Id }, result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGamePlayedStatus(int id)
    {
        var item = await _context.GamePlayedStatuses.FindAsync(id);
        if (item == null) return NotFound();

        _context.GamePlayedStatuses.Remove(item);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderPlayedStatuses([FromBody] ReorderStatusesDto reorderDto)
    {
        if (reorderDto?.OrderedIds == null || reorderDto.OrderedIds.Count == 0)
        {
            return BadRequest("Se requiere la lista ordenada de IDs");
        }

        var allStatuses = await _context.GamePlayedStatuses.ToListAsync();
        
        for (int i = 0; i < reorderDto.OrderedIds.Count; i++)
        {
            var status = allStatuses.FirstOrDefault(s => s.Id == reorderDto.OrderedIds[i]);
            if (status != null)
            {
                status.SortOrder = i + 1;
            }
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }
}