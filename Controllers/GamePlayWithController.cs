using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamePlayWithController : BaseApiController
{
    private readonly GamesDbContext _context;

    public GamePlayWithController(GamesDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GamePlayWithDto>>> GetGamePlayWiths([FromQuery] QueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);
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
                _ => query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE")) // Default: orden personalizado
            };
        }
        else
        {
            query = query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE")); // Default: orden personalizado
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
        var userId = GetCurrentUserIdOrDefault(1);
        var items = await _context.GamePlayWiths
            .Where(p => p.IsActive && p.UserId == userId)
            .OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE"))
            .ToListAsync();

        return Ok(items.Select(p => p.ToDto()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GamePlayWithDto>> GetGamePlayWith(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var item = await _context.GamePlayWiths
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        return item == null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGamePlayWith(int id, GamePlayWithUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var item = await _context.GamePlayWiths
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (item == null)
            return NotFound(new { message = "Elemento no encontrado" });

        if (await _context.GamePlayWiths.AnyAsync(p => p.Name.ToLower() == updateDto.Name.ToLower() && p.Id != id && p.UserId == userId))
        {
            return Conflict(new { message = "Ya existe un elemento con este nombre" });
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
                return NotFound(new { message = "Elemento no encontrado" });
            else
                return Conflict(new { message = "Conflicto de concurrencia" });
        }

        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<GamePlayWithDto>> PostGamePlayWith(GamePlayWithCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        if (await _context.GamePlayWiths.AnyAsync(p => p.Name.ToLower() == createDto.Name.ToLower() && p.UserId == userId))
        {
            return Conflict(new { message = "Ya existe un elemento con este nombre" });
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

        var result = item.ToDto();
        return CreatedAtAction("GetGamePlayWith", new { id = item.Id }, result);
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderPlayWith([FromBody] ReorderStatusesDto dto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        if (dto?.OrderedIds == null || dto.OrderedIds.Count == 0)
            return BadRequest(new { message = "OrderedIds must be provided" });

        var items = await _context.GamePlayWiths
            .Where(p => dto.OrderedIds.Contains(p.Id) && p.UserId == userId)
            .ToListAsync();

        if (items.Count != dto.OrderedIds.Count)
        {
            return NotFound(new { message = "One or more play-with IDs not found" });
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            for (int i = 0; i < dto.OrderedIds.Count; i++)
            {
                var id = dto.OrderedIds[i];
                var item = items.First(p => p.Id == id);
                item.SortOrder = i + 1; // 1-based ordering
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return Ok(new { message = "Play-with options reordered successfully" });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { message = "Error reordering play-with options" });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGamePlayWith(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var item = await _context.GamePlayWiths
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (item == null)
            return NotFound(new { message = "Elemento no encontrado" });

        // Verificar si hay juegos usando este PlayWith
        var gamesUsingPlayWith = await _context.GamePlayWithMappings
            .Where(m => m.PlayWithId == id)
            .Join(_context.Games, m => m.GameId, g => g.Id, (m, g) => g)
            .CountAsync(g => g.UserId == userId);

        if (gamesUsingPlayWith > 0)
        {
            return BadRequest(new
            {
                message = "No se puede eliminar el elemento",
                details = $"Hay {gamesUsingPlayWith} juego(s) que usan este elemento",
                gamesCount = gamesUsingPlayWith
            });
        }

        _context.GamePlayWiths.Remove(item);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}