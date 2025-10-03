using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamePlayedStatusController : BaseApiController
{
    private readonly GamesDbContext _context;

    public GamePlayedStatusController(GamesDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GamePlayedStatusDto>>> GetGamePlayedStatuses([FromQuery] QueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);
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
        var userId = GetCurrentUserIdOrDefault(1);
        var items = await _context.GamePlayedStatuses
            .Where(p => p.IsActive && p.UserId == userId)
            .OrderBy(p => EF.Functions.Collate(p.Name, "NOCASE"))
            .ToListAsync();

        return Ok(items.Select(p => p.ToDto()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GamePlayedStatusDto>> GetGamePlayedStatus(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var item = await _context.GamePlayedStatuses
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        return item == null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGamePlayedStatus(int id, GamePlayedStatusUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var item = await _context.GamePlayedStatuses
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (item == null)
            return NotFound(new { message = "Estado no encontrado" });

        if (await _context.GamePlayedStatuses.AnyAsync(p => p.Name.ToLower() == updateDto.Name.ToLower() && p.Id != id && p.UserId == userId))
        {
            return Conflict(new { message = "Ya existe un estado con este nombre" });
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
                return NotFound(new { message = "Estado no encontrado" });
            else
                return Conflict(new { message = "Conflicto de concurrencia" });
        }

        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<GamePlayedStatusDto>> PostGamePlayedStatus(GamePlayedStatusCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        if (await _context.GamePlayedStatuses.AnyAsync(p => p.Name.ToLower() == createDto.Name.ToLower() && p.UserId == userId))
        {
            return Conflict(new { message = "Ya existe un estado con este nombre" });
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

        var result = item.ToDto();
        return CreatedAtAction("GetGamePlayedStatus", new { id = item.Id }, result);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGamePlayedStatus(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var item = await _context.GamePlayedStatuses
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (item == null)
            return NotFound(new { message = "Estado no encontrado" });

        // Verificar si hay juegos usando este PlayedStatus
        var gamesUsingPlayedStatus = await _context.Games.CountAsync(g => g.PlayedStatusId == id && g.UserId == userId);
        if (gamesUsingPlayedStatus > 0)
        {
            return BadRequest(new
            {
                message = "No se puede eliminar el estado",
                details = $"Hay {gamesUsingPlayedStatus} juego(s) que usan este estado",
                gamesCount = gamesUsingPlayedStatus
            });
        }

        _context.GamePlayedStatuses.Remove(item);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderPlayedStatuses([FromBody] ReorderStatusesDto dto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        if (dto?.OrderedIds == null || dto.OrderedIds.Count == 0)
            return BadRequest(new { message = "OrderedIds must be provided" });

        var statuses = await _context.GamePlayedStatuses
            .Where(s => dto.OrderedIds.Contains(s.Id) && s.UserId == userId)
            .ToListAsync();

        if (statuses.Count != dto.OrderedIds.Count)
        {
            return NotFound(new { message = "One or more played status IDs not found" });
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

            return Ok(new { message = "Played statuses reordered successfully" });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { message = "Error reordering played statuses" });
        }
    }
}