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
[Authorize]
public class GameReplayTypesController : BaseApiController
{
    private readonly GamesDbContext _context;

    public GameReplayTypesController(GamesDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GameReplayTypeDto>>> GetGameReplayTypes([FromQuery] QueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);
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

        return Ok(new PagedResult<GameReplayTypeDto>
        {
            Data = items.Select(t => t.ToDto()),
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<GameReplayTypeDto>>> GetActiveGameReplayTypes()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var items = await _context.GameReplayTypes
            .Where(t => t.IsActive && t.UserId == userId)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => EF.Functions.Collate(t.Name, "NOCASE"))
            .ToListAsync();

        return Ok(items.Select(t => t.ToDto()));
    }

    [HttpGet("special")]
    public async Task<ActionResult<GameReplayTypeDto>> GetSpecialGameReplayType()
    {
        var userId = GetCurrentUserIdOrDefault(1);
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

        return Ok(item.ToDto());
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GameReplayTypeDto>> GetGameReplayType(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var item = await _context.GameReplayTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        return item == null ? NotFound() : Ok(item.ToDto());
    }

    [HttpPost]
    public async Task<ActionResult<GameReplayTypeDto>> PostGameReplayType(GameReplayTypeCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        if (await _context.GameReplayTypes.AnyAsync(t => t.Name.ToLower() == createDto.Name.ToLower() && t.UserId == userId))
            return Conflict(new { message = "Ya existe un tipo con este nombre" });

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

        return CreatedAtAction(nameof(GetGameReplayType), new { id = item.Id }, item.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGameReplayType(int id, GameReplayTypeUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var item = await _context.GameReplayTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (item == null)
            return NotFound(new { message = "Tipo no encontrado" });

        if (await _context.GameReplayTypes.AnyAsync(t => t.Name.ToLower() == updateDto.Name.ToLower() && t.Id != id && t.UserId == userId))
            return Conflict(new { message = "Ya existe un tipo con este nombre" });

        item.Name = updateDto.Name;
        item.IsActive = updateDto.IsActive;
        item.Color = updateDto.Color;
        if (updateDto.SortOrder.HasValue) item.SortOrder = updateDto.SortOrder.Value;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGameReplayType(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var item = await _context.GameReplayTypes
            .FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId);

        if (item == null)
            return NotFound(new { message = "Tipo no encontrado" });

        if (item.IsSpecialType)
            return Conflict(new { message = "No se puede eliminar el tipo especial de rejugada" });

        // Reasignar replays al tipo especial antes de borrar
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

        return NoContent();
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderGameReplayTypes([FromBody] ReorderReplayTypesDto reorderDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var types = await _context.GameReplayTypes
            .Where(t => t.UserId == userId && reorderDto.OrderedIds.Contains(t.Id))
            .ToListAsync();

        for (int i = 0; i < reorderDto.OrderedIds.Count; i++)
        {
            var type = types.FirstOrDefault(t => t.Id == reorderDto.OrderedIds[i]);
            if (type != null) type.SortOrder = i + 1;
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }
}
