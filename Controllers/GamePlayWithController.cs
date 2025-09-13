using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamePlayWithController : ControllerBase
{
    private readonly GamesDbContext _context;

    public GamePlayWithController(GamesDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GamePlayWithDto>>> GetGamePlayWiths([FromQuery] QueryParameters parameters)
    {
        var query = _context.GamePlayWiths.AsQueryable();

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
                _ => query.OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE")) // Default: orden alfabético case-insensitive
            };
        }
        else
        {
            query = query.OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE")); // Default: orden alfabético case-insensitive
        }

        var totalCount = await query.CountAsync();
        var items = await query.Skip(parameters.Skip).Take(parameters.Take).ToListAsync();

        return Ok(new PagedResult<GamePlayWithDto>
        {
            Data = items.Select(p => p.ToDto()),
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<GamePlayWithDto>>> GetActiveGamePlayWiths()
    {
        var items = await _context.GamePlayWiths
            .Where(p => p.IsActive)
            .OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE")) // Orden alfabético case-insensitive
            .ToListAsync();

        return Ok(items.Select(p => p.ToDto()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GamePlayWithDto>> GetGamePlayWith(int id)
    {
        var item = await _context.GamePlayWiths.FindAsync(id);
        return item == null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGamePlayWith(int id, GamePlayWithUpdateDto updateDto)
    {
        var item = await _context.GamePlayWiths.FindAsync(id);
        if (item == null)
        {
            return NotFound(new
            {
                message = "Elemento no encontrado",
                details = "El elemento que intenta actualizar no existe."
            });
        }

        // Validar que el nombre no exista en otro elemento
        if (await _context.GamePlayWiths.AnyAsync(p => p.Name.ToLower() == updateDto.Name.ToLower() && p.Id != id))
        {
            return Conflict(new
            {
                message = "Ya existe un elemento con este nombre",
                details = "El nombre debe ser único. Por favor, use un nombre diferente."
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
            if (!_context.GamePlayWiths.Any(e => e.Id == id))
            {
                return NotFound(new
                {
                    message = "Elemento no encontrado",
                    details = "El elemento fue eliminado por otro usuario."
                });
            }
            else
            {
                return Conflict(new
                {
                    message = "Conflicto de concurrencia",
                    details = "El elemento fue modificado por otro usuario. Por favor, recargue e intente nuevamente."
                });
            }
        }

        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<GamePlayWithDto>> PostGamePlayWith(GamePlayWithCreateDto createDto)
    {
        // Validar que el nombre no exista
        if (await _context.GamePlayWiths.AnyAsync(p => p.Name.ToLower() == createDto.Name.ToLower()))
        {
            return Conflict(new
            {
                message = "Ya existe un elemento con este nombre",
                details = "El nombre debe ser único. Por favor, use un nombre diferente."
            });
        }

        var item = new GamePlayWith
        {
            Name = createDto.Name,
            SortOrder = 0, // Se mantiene para compatibilidad con la base de datos, pero no se usa
            IsActive = createDto.IsActive,
            Color = createDto.Color
        };

        _context.GamePlayWiths.Add(item);
        await _context.SaveChangesAsync();

        var result = item.ToDto();
        return CreatedAtAction("GetGamePlayWith", new { id = item.Id }, result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGamePlayWith(int id)
    {
        var item = await _context.GamePlayWiths.FindAsync(id);
        if (item == null) return NotFound();

        _context.GamePlayWiths.Remove(item);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}