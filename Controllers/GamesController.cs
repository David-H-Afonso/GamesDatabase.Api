using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly GamesDbContext _context;

    public GamesController(GamesDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Obtiene todos los juegos con paginado, filtrado y ordenado
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<GameDto>>> GetGames([FromQuery] GameQueryParameters parameters)
    {
        var query = _context.Games
            .Include(g => g.Status)
            .Include(g => g.Platform)
            .Include(g => g.PlayWith)
            .Include(g => g.PlayedStatus)
            .AsQueryable();

        // Aplicar filtros
        if (!string.IsNullOrEmpty(parameters.Search))
        {
            query = query.Where(g => g.Name.Contains(parameters.Search) ||
                                   (g.Comment != null && g.Comment.Contains(parameters.Search)));
        }

        if (parameters.StatusId.HasValue)
        {
            query = query.Where(g => g.StatusId == parameters.StatusId.Value);
        }

        if (parameters.PlatformId.HasValue)
        {
            query = query.Where(g => g.PlatformId == parameters.PlatformId.Value);
        }

        if (parameters.PlayWithId.HasValue)
        {
            query = query.Where(g => g.PlayWithId == parameters.PlayWithId.Value);
        }

        if (parameters.PlayedStatusId.HasValue)
        {
            query = query.Where(g => g.PlayedStatusId == parameters.PlayedStatusId.Value);
        }

        if (parameters.MinGrade.HasValue)
        {
            query = query.Where(g => g.Grade >= parameters.MinGrade.Value);
        }

        if (parameters.MaxGrade.HasValue)
        {
            query = query.Where(g => g.Grade <= parameters.MaxGrade.Value);
        }

        if (!string.IsNullOrEmpty(parameters.Released))
        {
            query = query.Where(g => g.Released != null && g.Released.Contains(parameters.Released));
        }

        if (!string.IsNullOrEmpty(parameters.Started))
        {
            query = query.Where(g => g.Started != null && g.Started.Contains(parameters.Started));
        }

        if (!string.IsNullOrEmpty(parameters.Finished))
        {
            query = query.Where(g => g.Finished != null && g.Finished.Contains(parameters.Finished));
        }

        // Aplicar ordenado
        if (!string.IsNullOrEmpty(parameters.SortBy))
        {
            query = parameters.SortBy.ToLower() switch
            {
                "name" => parameters.SortDescending ?
                    query.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE")).Reverse() :
                    query.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "grade" => parameters.SortDescending ? query.OrderByDescending(g => g.Grade) : query.OrderBy(g => g.Grade),
                "critic" => parameters.SortDescending ? query.OrderByDescending(g => g.Critic) : query.OrderBy(g => g.Critic),
                "story" => parameters.SortDescending ? query.OrderByDescending(g => g.Story) : query.OrderBy(g => g.Story),
                "completion" => parameters.SortDescending ? query.OrderByDescending(g => g.Completion) : query.OrderBy(g => g.Completion),
                "score" => parameters.SortDescending ? query.OrderByDescending(g => g.Score) : query.OrderBy(g => g.Score),
                "released" => parameters.SortDescending ? query.OrderByDescending(g => g.Released) : query.OrderBy(g => g.Released),
                "started" => parameters.SortDescending ? query.OrderByDescending(g => g.Started) : query.OrderBy(g => g.Started),
                "finished" => parameters.SortDescending ? query.OrderByDescending(g => g.Finished) : query.OrderBy(g => g.Finished),
                "creation" or "id" => parameters.SortDescending ? query.OrderByDescending(g => g.Id) : query.OrderBy(g => g.Id),
                _ => query.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE")) // Default: orden alfabético case-insensitive
            };
        }
        else
        {
            query = query.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE")); // Default: orden alfabético case-insensitive
        }

        var totalCount = await query.CountAsync();

        var games = await query
            .Skip(parameters.Skip)
            .Take(parameters.Take)
            .ToListAsync();

        var gameDtos = games.Select(g => g.ToDto()).ToList();

        return Ok(new PagedResult<GameDto>
        {
            Data = gameDtos,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }

    /// <summary>
    /// Obtiene un juego específico por ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<GameDto>> GetGame(int id)
    {
        var game = await _context.Games
            .Include(g => g.Status)
            .Include(g => g.Platform)
            .Include(g => g.PlayWith)
            .Include(g => g.PlayedStatus)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (game == null)
        {
            return NotFound();
        }

        return Ok(game.ToDto());
    }

    /// <summary>
    /// Actualiza un juego existente
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> PutGame(int id, GameUpdateDto gameDto)
    {
        var game = await _context.Games.FindAsync(id);
        if (game == null)
        {
            return NotFound();
        }

        game.UpdateEntity(gameDto);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!GameExists(id))
            {
                return NotFound();
            }
            else
            {
                throw;
            }
        }

        return NoContent();
    }

    /// <summary>
    /// Crea un nuevo juego
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<GameDto>> PostGame(GameCreateDto gameDto)
    {
        var game = gameDto.ToEntity();
        _context.Games.Add(game);
        await _context.SaveChangesAsync();

        // Cargar las relaciones para el DTO de respuesta
        await _context.Entry(game)
            .Reference(g => g.Status)
            .LoadAsync();
        await _context.Entry(game)
            .Reference(g => g.Platform)
            .LoadAsync();
        await _context.Entry(game)
            .Reference(g => g.PlayWith)
            .LoadAsync();
        await _context.Entry(game)
            .Reference(g => g.PlayedStatus)
            .LoadAsync();

        return CreatedAtAction("GetGame", new { id = game.Id }, game.ToDto());
    }

    /// <summary>
    /// Elimina un juego
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGame(int id)
    {
        var game = await _context.Games.FindAsync(id);
        if (game == null)
        {
            return NotFound();
        }

        _context.Games.Remove(game);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool GameExists(int id)
    {
        return _context.Games.Any(e => e.Id == id);
    }
}