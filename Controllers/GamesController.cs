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

        if (parameters.ExcludeStatusIds?.Length > 0)
        {
            query = query.Where(g => !parameters.ExcludeStatusIds.Contains(g.StatusId));
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
                "name" => parameters.SortDescending ? query.OrderByDescending(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "grade" => parameters.SortDescending ? query.OrderByDescending(g => g.Grade).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Grade).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "critic" => parameters.SortDescending ? query.OrderByDescending(g => g.Critic).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Critic).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "story" => parameters.SortDescending ? query.OrderByDescending(g => g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "storyduration" => parameters.SortDescending ? query.OrderByDescending(g => (double?)g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => (double?)g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "completion" => parameters.SortDescending ? query.OrderByDescending(g => g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "completionduration" => parameters.SortDescending ? query.OrderByDescending(g => (double?)g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => (double?)g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "status" => parameters.SortDescending ? query.OrderByDescending(g => g.Status.SortOrder).ThenBy(g => EF.Functions.Collate(g.Status.Name, "NOCASE")).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Status.SortOrder).ThenBy(g => EF.Functions.Collate(g.Status.Name, "NOCASE")).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "score" => parameters.SortDescending ? query.OrderByDescending(g => (double?)g.Score).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => (double?)g.Score).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "released" => parameters.SortDescending ? query.OrderByDescending(g => g.Released).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Released).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "started" => parameters.SortDescending ? query.OrderByDescending(g => g.Started).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Started).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "finished" => parameters.SortDescending ? query.OrderByDescending(g => g.Finished).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Finished).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "createdat" or "created" => parameters.SortDescending ? query.OrderByDescending(g => g.CreatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.CreatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "updatedat" or "updated" or "lastedited" => parameters.SortDescending ? query.OrderByDescending(g => g.UpdatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.UpdatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
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
    public async Task<IActionResult> PutGame(int id, [FromBody] System.Text.Json.JsonElement gameDto)
    {
        var game = await _context.Games.FindAsync(id);
        if (game == null)
        {
            return NotFound();
        }

        // Actualizar solo los campos que están presentes en el JSON
        if (gameDto.TryGetProperty("statusId", out var statusIdElement) && statusIdElement.ValueKind != System.Text.Json.JsonValueKind.Null)
        {
            game.StatusId = statusIdElement.GetInt32();
        }

        if (gameDto.TryGetProperty("name", out var nameElement) && nameElement.ValueKind != System.Text.Json.JsonValueKind.Null)
        {
            game.Name = nameElement.GetString() ?? string.Empty;
        }

        // Campos opcionales que se pueden eliminar (establecer a null)
        if (gameDto.TryGetProperty("grade", out var gradeElement))
        {
            game.Grade = gradeElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : gradeElement.GetInt32();
        }

        if (gameDto.TryGetProperty("critic", out var criticElement))
        {
            game.Critic = criticElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : criticElement.GetInt32();
        }

        if (gameDto.TryGetProperty("story", out var storyElement))
        {
            game.Story = storyElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : storyElement.GetInt32();
        }

        if (gameDto.TryGetProperty("completion", out var completionElement))
        {
            game.Completion = completionElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : completionElement.GetInt32();
        }

        if (gameDto.TryGetProperty("platformId", out var platformIdElement))
        {
            game.PlatformId = platformIdElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : platformIdElement.GetInt32();
        }

        if (gameDto.TryGetProperty("released", out var releasedElement))
        {
            game.Released = releasedElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : releasedElement.GetString();
        }

        if (gameDto.TryGetProperty("started", out var startedElement))
        {
            game.Started = startedElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : startedElement.GetString();
        }

        if (gameDto.TryGetProperty("finished", out var finishedElement))
        {
            game.Finished = finishedElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : finishedElement.GetString();
        }

        if (gameDto.TryGetProperty("comment", out var commentElement))
        {
            game.Comment = commentElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : commentElement.GetString();
        }

        if (gameDto.TryGetProperty("playWithId", out var playWithIdElement))
        {
            game.PlayWithId = playWithIdElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : playWithIdElement.GetInt32();
        }

        if (gameDto.TryGetProperty("playedStatusId", out var playedStatusIdElement))
        {
            game.PlayedStatusId = playedStatusIdElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : playedStatusIdElement.GetInt32();
        }

        if (gameDto.TryGetProperty("logo", out var logoElement))
        {
            game.Logo = logoElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : logoElement.GetString();
        }

        if (gameDto.TryGetProperty("cover", out var coverElement))
        {
            game.Cover = coverElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : coverElement.GetString();
        }

        // Recalcular el score
        game.CalculateScore();

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

    // Apply the same filtering logic used by GetGames (without ordering or pagination)
    private IQueryable<Models.Game> ApplyCommonFilters(IQueryable<Models.Game> query, GameQueryParameters parameters)
    {
        if (!string.IsNullOrEmpty(parameters.Search))
        {
            query = query.Where(g => g.Name.Contains(parameters.Search) || (g.Comment != null && g.Comment.Contains(parameters.Search)));
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

        return query;
    }

    /// <summary>
    /// Vista: juegos que empezaron en un año dado Y/O que tienen un status que coincida
    /// Parámetros: year (int, required), status (string, optional)
    /// Devuelve juegos donde (Started year == year) OR (Status.Name LIKE %status%)
    /// </summary>
    [HttpGet("started-or-status")]
    public async Task<ActionResult<PagedResult<GameDto>>> GetStartedOrStatus([FromQuery] GameQueryParameters parameters, [FromQuery] int year, [FromQuery] string? status)
    {
        if (year <= 0) return BadRequest("year is required and must be a positive integer");

        // base query with common filters
        var baseQuery = _context.Games
            .Include(g => g.Status)
            .Include(g => g.Platform)
            .Include(g => g.PlayWith)
            .Include(g => g.PlayedStatus)
            .AsQueryable();

        baseQuery = ApplyCommonFilters(baseQuery, parameters);

        // started matches
        var startedMatch = baseQuery.Where(g => g.Started != null && (
            g.Started == year.ToString() ||
            g.Started.StartsWith(year + "-") ||
            g.Started.Contains("/" + year.ToString() + "/") ||
            g.Started.Contains("-" + year.ToString() + "-") ||
            g.Started.EndsWith("/" + year)
        ));

        IQueryable<Models.Game> finalQuery;

        if (string.IsNullOrWhiteSpace(status))
        {
            finalQuery = startedMatch;
        }
        else
        {
            var statusTrim = status.Trim();
            var statusMatch = baseQuery.Where(g => g.Status != null && EF.Functions.Like(g.Status.Name, "%" + statusTrim + "%"));
            finalQuery = startedMatch.Union(statusMatch);
        }

        // apply ordering
        if (!string.IsNullOrEmpty(parameters.SortBy))
        {
            finalQuery = parameters.SortBy.ToLower() switch
            {
                "name" => parameters.SortDescending ? finalQuery.OrderByDescending(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "grade" => parameters.SortDescending ? finalQuery.OrderByDescending(g => g.Grade).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => g.Grade).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "critic" => parameters.SortDescending ? finalQuery.OrderByDescending(g => g.Critic).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => g.Critic).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "story" => parameters.SortDescending ? finalQuery.OrderByDescending(g => g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "storyduration" => parameters.SortDescending ? finalQuery.OrderByDescending(g => (double?)g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => (double?)g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "completion" => parameters.SortDescending ? finalQuery.OrderByDescending(g => g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "completionduration" => parameters.SortDescending ? finalQuery.OrderByDescending(g => (double?)g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => (double?)g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "status" => parameters.SortDescending ? finalQuery.OrderByDescending(g => g.Status.SortOrder).ThenByDescending(g => EF.Functions.Collate(g.Status.Name, "NOCASE")).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => g.Status.SortOrder).ThenBy(g => EF.Functions.Collate(g.Status.Name, "NOCASE")).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "score" => parameters.SortDescending ? finalQuery.OrderByDescending(g => (double?)g.Score).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => (double?)g.Score).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "released" => parameters.SortDescending ? finalQuery.OrderByDescending(g => g.Released).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => g.Released).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "started" => parameters.SortDescending ? finalQuery.OrderByDescending(g => g.Started).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => g.Started).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "finished" => parameters.SortDescending ? finalQuery.OrderByDescending(g => g.Finished).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : finalQuery.OrderBy(g => g.Finished).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "creation" or "id" => parameters.SortDescending ? finalQuery.OrderByDescending(g => g.Id) : finalQuery.OrderBy(g => g.Id),
                _ => finalQuery.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE"))
            };
        }
        else
        {
            finalQuery = finalQuery.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE"));
        }

        var totalCount = await finalQuery.CountAsync();

        var games = await finalQuery.Skip(parameters.Skip).Take(parameters.Take).ToListAsync();
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
    /// Vista: juegos cuyo `Released` es el año dado Y ademas `Started` es el mismo año
    /// Parámetros: year (int, required)
    /// </summary>
    [HttpGet("released-and-started")]
    public async Task<ActionResult<PagedResult<GameDto>>> GetReleasedAndStarted([FromQuery] GameQueryParameters parameters, [FromQuery] int year)
    {
        if (year <= 0) return BadRequest("year is required and must be a positive integer");

        var baseQuery = _context.Games
            .Include(g => g.Status)
            .Include(g => g.Platform)
            .Include(g => g.PlayWith)
            .Include(g => g.PlayedStatus)
            .AsQueryable();

        baseQuery = ApplyCommonFilters(baseQuery, parameters);

        // released matches
        baseQuery = baseQuery.Where(g => g.Released != null && (
            g.Released == year.ToString() ||
            g.Released.StartsWith(year + "-") ||
            g.Released.Contains("/" + year.ToString() + "/") ||
            g.Released.Contains("-" + year.ToString() + "-") ||
            g.Released.EndsWith("/" + year)
        ));

        // and started matches same year
        baseQuery = baseQuery.Where(g => g.Started != null && (
            g.Started == year.ToString() ||
            g.Started.StartsWith(year + "-") ||
            g.Started.Contains("/" + year.ToString() + "/") ||
            g.Started.Contains("-" + year.ToString() + "-") ||
            g.Started.EndsWith("/" + year)
        ));

        // apply ordering
        if (!string.IsNullOrEmpty(parameters.SortBy))
        {
            baseQuery = parameters.SortBy.ToLower() switch
            {
                "name" => parameters.SortDescending ? baseQuery.OrderByDescending(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "grade" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Grade).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => g.Grade).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "critic" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Critic).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => g.Critic).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "story" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "storyduration" => parameters.SortDescending ? baseQuery.OrderByDescending(g => (double?)g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => (double?)g.Story).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "completion" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "completionduration" => parameters.SortDescending ? baseQuery.OrderByDescending(g => (double?)g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => (double?)g.Completion).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "status" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Status.SortOrder).ThenByDescending(g => EF.Functions.Collate(g.Status.Name, "NOCASE")).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => g.Status.SortOrder).ThenBy(g => EF.Functions.Collate(g.Status.Name, "NOCASE")).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "score" => parameters.SortDescending ? baseQuery.OrderByDescending(g => (double?)g.Score).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => (double?)g.Score).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "released" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Released).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => g.Released).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "started" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Started).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => g.Started).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "finished" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Finished).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : baseQuery.OrderBy(g => g.Finished).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "creation" or "id" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Id) : baseQuery.OrderBy(g => g.Id),
                _ => baseQuery.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE"))
            };
        }
        else
        {
            baseQuery = baseQuery.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE"));
        }

        var totalCount = await baseQuery.CountAsync();
        var games = await baseQuery.Skip(parameters.Skip).Take(parameters.Take).ToListAsync();
        var dtos = games.Select(g => g.ToDto()).ToList();
        return Ok(new PagedResult<GameDto>
        {
            Data = dtos,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }

    /// <summary>
    /// Vista: juegos sin fecha `Started` (null/empty) ordenados por `Score` descendente
    /// Acepta todos los `GameQueryParameters` para filtrar antes de ordenar/paginar
    /// </summary>
    [HttpGet("no-started-by-score")]
    public async Task<ActionResult<PagedResult<GameDto>>> GetNoStartedByScore([FromQuery] GameQueryParameters parameters)
    {
        var baseQuery = _context.Games
            .Include(g => g.Status)
            .Include(g => g.Platform)
            .Include(g => g.PlayWith)
            .Include(g => g.PlayedStatus)
            .AsQueryable();

        baseQuery = ApplyCommonFilters(baseQuery, parameters);

        // Filter where Started is null or empty
        baseQuery = baseQuery.Where(g => string.IsNullOrEmpty(g.Started));

        // Exclude certain statuses from this view (case-insensitive)
        baseQuery = baseQuery.Where(g => g.Status == null || (
            EF.Functions.Collate(g.Status.Name, "NOCASE") != "Always Playing" &&
            EF.Functions.Collate(g.Status.Name, "NOCASE") != "Achievements"
        ));

        // Apply ordering: if client requested a sort, use it; otherwise default to Score desc (nulls last)
        if (!string.IsNullOrEmpty(parameters.SortBy))
        {
            baseQuery = parameters.SortBy.ToLower() switch
            {
                "name" => parameters.SortDescending ? baseQuery.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE")).Reverse() : baseQuery.OrderBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "grade" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Grade) : baseQuery.OrderBy(g => g.Grade),
                "critic" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Critic) : baseQuery.OrderBy(g => g.Critic),
                "story" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Story) : baseQuery.OrderBy(g => g.Story),
                "storyduration" => parameters.SortDescending ? baseQuery.OrderByDescending(g => (double?)g.Story) : baseQuery.OrderBy(g => (double?)g.Story),
                "completion" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Completion) : baseQuery.OrderBy(g => g.Completion),
                "completionduration" => parameters.SortDescending ? baseQuery.OrderByDescending(g => (double?)g.Completion) : baseQuery.OrderBy(g => (double?)g.Completion),
                "status" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Status.SortOrder).ThenByDescending(g => EF.Functions.Collate(g.Status.Name, "NOCASE")) : baseQuery.OrderBy(g => g.Status.SortOrder).ThenBy(g => EF.Functions.Collate(g.Status.Name, "NOCASE")),
                "score" => parameters.SortDescending ? baseQuery.OrderByDescending(g => (double?)g.Score) : baseQuery.OrderBy(g => (double?)g.Score),
                "released" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Released) : baseQuery.OrderBy(g => g.Released),
                "started" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Started) : baseQuery.OrderBy(g => g.Started),
                "finished" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Finished) : baseQuery.OrderBy(g => g.Finished),
                "creation" or "id" => parameters.SortDescending ? baseQuery.OrderByDescending(g => g.Id) : baseQuery.OrderBy(g => g.Id),
                _ => baseQuery.OrderByDescending(g => g.Score != null).ThenByDescending(g => (double?)g.Score)
            };
        }
        else
        {
            // Default: Order by Score desc (nulls last)
            baseQuery = baseQuery.OrderByDescending(g => g.Score != null).ThenByDescending(g => (double?)g.Score);
        }

        var totalCount = await baseQuery.CountAsync();
        var games = await baseQuery.Skip(parameters.Skip).Take(parameters.Take).ToListAsync();
        var dtos = games.Select(g => g.ToDto()).ToList();

        return Ok(new PagedResult<GameDto>
        {
            Data = dtos,
            TotalCount = totalCount,
            Page = parameters.Page,
            PageSize = parameters.PageSize
        });
    }
}