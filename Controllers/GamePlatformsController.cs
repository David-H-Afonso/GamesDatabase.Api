using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.Models;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamePlatformsController : BaseApiController
{
    private readonly GamesDbContext _context;

    public GamePlatformsController(GamesDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GamePlatformDto>>> GetGamePlatforms([FromQuery] QueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var query = _context.GamePlatforms.Where(p => p.UserId == userId).AsQueryable();
        if (!string.IsNullOrEmpty(parameters.Search))
        {
            query = query.Where(p => p.Name.Contains(parameters.Search));
        }

        if (parameters.IsActive.HasValue)
        {
            query = query.Where(p => p.IsActive == parameters.IsActive.Value);
        }

        // Aplicar ordenado
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
                _ => query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE")) // Default: use SortOrder then name
            };
        }
        else
        {
            query = query.OrderBy(p => p.SortOrder).ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE")); // Default: use SortOrder then name
        }

        var totalCount = await query.CountAsync();

        var platforms = await query
            .Skip(parameters.Skip)
            .Take(parameters.Take)
            .ToListAsync();

        var platformDtos = platforms.Select(p => p.ToDto()).ToList();

        return Ok(new PagedResult<GamePlatformDto>
        {
            Data = platformDtos,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }

    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<GamePlatformDto>>> GetActiveGamePlatforms()
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var platforms = await _context.GamePlatforms
            .Where(p => p.IsActive && p.UserId == userId)
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => EF.Functions.Collate(p.Name, "NOCASE"))
            .ToListAsync();

        return Ok(platforms.Select(p => p.ToDto()));
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GamePlatformDto>> GetGamePlatform(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var gamePlatform = await _context.GamePlatforms
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (gamePlatform == null)
            return NotFound();

        return Ok(gamePlatform.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGamePlatform(int id, GamePlatformUpdateDto updateDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var gamePlatform = await _context.GamePlatforms
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (gamePlatform == null)
            return NotFound(new { message = "Plataforma no encontrada" });

        if (await _context.GamePlatforms.AnyAsync(p => p.Name.ToLower() == updateDto.Name.ToLower() && p.Id != id && p.UserId == userId))
        {
            return Conflict(new { message = "Ya existe una plataforma con este nombre" });
        }

        gamePlatform.Name = updateDto.Name;
        gamePlatform.IsActive = updateDto.IsActive;
        gamePlatform.Color = updateDto.Color;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!GamePlatformExists(id))
                return NotFound(new { message = "Plataforma no encontrada" });
            else
                return Conflict(new { message = "Conflicto de concurrencia" });
        }

        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<GamePlatformDto>> PostGamePlatform(GamePlatformCreateDto createDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        if (await _context.GamePlatforms.AnyAsync(p => p.Name.ToLower() == createDto.Name.ToLower() && p.UserId == userId))
        {
            return Conflict(new { message = "Ya existe una plataforma con este nombre" });
        }

        var gamePlatform = new GamePlatform
        {
            UserId = userId,
            Name = createDto.Name,
            SortOrder = 0,
            IsActive = createDto.IsActive,
            Color = createDto.Color
        };

        _context.GamePlatforms.Add(gamePlatform);
        await _context.SaveChangesAsync();

        var result = gamePlatform.ToDto();
        return CreatedAtAction("GetGamePlatform", new { id = gamePlatform.Id }, result);
    }

    [HttpPost("reorder")]
    public async Task<IActionResult> ReorderPlatforms([FromBody] ReorderStatusesDto dto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        if (dto?.OrderedIds == null || dto.OrderedIds.Count == 0)
            return BadRequest(new { message = "OrderedIds debe ser proporcionado" });

        var platforms = await _context.GamePlatforms
            .Where(p => dto.OrderedIds.Contains(p.Id) && p.UserId == userId)
            .ToListAsync();

        if (platforms.Count != dto.OrderedIds.Count)
        {
            return BadRequest(new { message = "Algunos IDs no existen" });
        }

        for (int i = 0; i < dto.OrderedIds.Count; i++)
        {
            var platform = platforms.FirstOrDefault(p => p.Id == dto.OrderedIds[i]);
            if (platform != null)
            {
                platform.SortOrder = i + 1;
            }
        }

        await _context.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGamePlatform(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var gamePlatform = await _context.GamePlatforms
            .FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);

        if (gamePlatform == null)
            return NotFound();

        _context.GamePlatforms.Remove(gamePlatform);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool GamePlatformExists(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        return _context.GamePlatforms.Any(e => e.Id == id && e.UserId == userId);
    }
}