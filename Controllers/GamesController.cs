using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;
using GamesDatabase.Api.Services;
using System.Text.Json;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GamesController : ControllerBase
{
    private readonly GamesDbContext _context;
    private readonly IViewFilterService _viewFilterService;

    public GamesController(GamesDbContext context, IViewFilterService viewFilterService)
    {
        _context = context;
        _viewFilterService = viewFilterService;
    }

    /// <summary>
    /// Obtiene todos los juegos con paginado, filtrado y ordenado
    /// Soporta aplicar vistas personalizadas que contienen filtros y ordenamientos complejos
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<GameDto>>> GetGames([FromQuery] GameQueryParameters parameters)
    {
        var query = _context.Games
            .Include(g => g.Status)
            .Include(g => g.Platform)
            .Include(g => g.GamePlayWiths)
                .ThenInclude(gpw => gpw.PlayWith)
            .Include(g => g.PlayedStatus)
            .AsQueryable();

        // Verificar si se debe aplicar una vista
        Models.ViewConfiguration? viewConfiguration = null;
        if (parameters.ViewId.HasValue || !string.IsNullOrEmpty(parameters.ViewName))
        {
            Models.GameView? gameView = null;

            if (parameters.ViewId.HasValue)
            {
                gameView = await _context.GameViews.FindAsync(parameters.ViewId.Value);
            }
            else if (!string.IsNullOrEmpty(parameters.ViewName))
            {
                gameView = await _context.GameViews
                    .FirstOrDefaultAsync(v => v.Name == parameters.ViewName);
            }

            if (gameView == null)
            {
                return BadRequest($"Vista no encontrada: {parameters.ViewId?.ToString() ?? parameters.ViewName}");
            }

            try
            {
                // FiltersJson historically stores an array of ViewFilter objects.
                // Try to deserialize as List<ViewFilter> first and wrap into a ViewConfiguration.
                if (!string.IsNullOrEmpty(gameView.FiltersJson))
                {
                    try
                    {
                        var filters = JsonSerializer.Deserialize<List<Models.ViewFilter>>(gameView.FiltersJson);
                        if (filters != null)
                        {
                            viewConfiguration = new Models.ViewConfiguration
                            {
                                FilterGroups = new List<Models.FilterGroup>
                                {
                                    new Models.FilterGroup { Filters = filters }
                                }
                            };
                        }
                        else
                        {
                            // If that fails, try to deserialize as full ViewConfiguration
                            viewConfiguration = JsonSerializer.Deserialize<Models.ViewConfiguration>(gameView.FiltersJson);
                        }
                    }
                    catch (JsonException)
                    {
                        // Fallback: try to deserialize as full configuration
                        viewConfiguration = JsonSerializer.Deserialize<Models.ViewConfiguration>(gameView.FiltersJson);
                    }
                }

                if (!string.IsNullOrEmpty(gameView.SortingJson))
                {
                    var sorting = JsonSerializer.Deserialize<List<Models.ViewSort>>(gameView.SortingJson);
                    if (sorting != null)
                    {
                        if (viewConfiguration == null) viewConfiguration = new Models.ViewConfiguration();
                        viewConfiguration.Sorting = sorting;
                    }
                }
            }
            catch (JsonException ex)
            {
                return BadRequest($"Error procesando configuración de vista: {ex.Message}");
            }
        }

        // Si hay configuración de vista, aplicarla; si no, usar filtros tradicionales
        if (viewConfiguration != null)
        {
            try
            {
                query = _viewFilterService.ApplyFilters(query, viewConfiguration);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error aplicando filtros de vista: {ex.Message}");
            }
        }
        else
        {
            // Aplicar filtros tradicionales
            query = ApplyTraditionalFilters(query, parameters);

            // Aplicar ordenado tradicional
            query = ApplyTraditionalSorting(query, parameters);
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
    /// Aplica filtros tradicionales cuando no se usa una vista
    /// </summary>
    private IQueryable<Models.Game> ApplyTraditionalFilters(IQueryable<Models.Game> query, GameQueryParameters parameters)
    {
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
            query = query.Where(g => g.GamePlayWiths.Any(gpw => gpw.PlayWithId == parameters.PlayWithId.Value));
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
    /// Aplica ordenamiento tradicional cuando no se usa una vista
    /// </summary>
    private IQueryable<Models.Game> ApplyTraditionalSorting(IQueryable<Models.Game> query, GameQueryParameters parameters)
    {
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

        return query;
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
            .Include(g => g.GamePlayWiths)
                .ThenInclude(gpw => gpw.PlayWith)
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

        if (gameDto.TryGetProperty("playWithIds", out var playWithIdsElement))
        {
            // Eliminar las relaciones existentes
            var existingMappings = await _context.GamePlayWithMappings
                .Where(gpw => gpw.GameId == id)
                .ToListAsync();
            _context.GamePlayWithMappings.RemoveRange(existingMappings);

            // Agregar las nuevas relaciones
            if (playWithIdsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                var playWithIds = playWithIdsElement.EnumerateArray()
                    .Select(e => e.GetInt32())
                    .ToList();

                foreach (var playWithId in playWithIds)
                {
                    _context.GamePlayWithMappings.Add(new Models.GamePlayWithMapping
                    {
                        GameId = id,
                        PlayWithId = playWithId
                    });
                }
            }

            // Marcar el juego como modificado para actualizar UpdatedAt
            _context.Entry(game).State = EntityState.Modified;
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

        // Agregar las relaciones PlayWith
        if (gameDto.PlayWithIds != null && gameDto.PlayWithIds.Any())
        {
            foreach (var playWithId in gameDto.PlayWithIds)
            {
                _context.GamePlayWithMappings.Add(new Models.GamePlayWithMapping
                {
                    GameId = game.Id,
                    PlayWithId = playWithId
                });
            }
            await _context.SaveChangesAsync();
        }

        // Cargar las relaciones para el DTO de respuesta
        await _context.Entry(game)
            .Reference(g => g.Status)
            .LoadAsync();
        await _context.Entry(game)
            .Reference(g => g.Platform)
            .LoadAsync();
        await _context.Entry(game)
            .Collection(g => g.GamePlayWiths)
            .LoadAsync();
        await _context.Entry(game)
            .Reference(g => g.PlayedStatus)
            .LoadAsync();

        // Cargar los PlayWith relacionados
        foreach (var gpw in game.GamePlayWiths)
        {
            await _context.Entry(gpw)
                .Reference(m => m.PlayWith)
                .LoadAsync();
        }

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