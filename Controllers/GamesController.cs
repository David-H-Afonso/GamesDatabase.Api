using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;
using GamesDatabase.Api.Services;
using System.Text.Json;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GamesController : BaseApiController
{
    private readonly GamesDbContext _context;
    private readonly IViewFilterService _viewFilterService;

    public GamesController(
        GamesDbContext context,
        IViewFilterService viewFilterService)
    {
        _context = context;
        _viewFilterService = viewFilterService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<GameDto>>> GetGames([FromQuery] GameQueryParameters parameters)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        var query = _context.Games
            .Where(g => g.UserId == userId)
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
                gameView = await _context.GameViews.AsNoTracking().FirstOrDefaultAsync(v => v.Id == parameters.ViewId.Value);
            }
            else if (!string.IsNullOrEmpty(parameters.ViewName))
            {
                gameView = await _context.GameViews
                    .AsNoTracking()
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

        if (viewConfiguration != null)
        {
            try
            {
                query = _viewFilterService.ApplyFilters(query, viewConfiguration, userId);
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
            var searchLower = parameters.Search.ToLower();
            var searchNoAccents = RemoveDiacritics(searchLower);

            // Generar variantes con tildes comunes (para que "poke" encuentre "poké")
            var searchWithE = searchNoAccents.Replace("e", "é");
            var searchWithO = searchNoAccents.Replace("o", "ó");
            var searchWithA = searchNoAccents.Replace("a", "á");
            var searchWithI = searchNoAccents.Replace("i", "í");
            var searchWithU = searchNoAccents.Replace("u", "ú");

            // Buscar con el término original, sin acentos, Y con variantes de tildes
            // Esto permite búsqueda bidireccional completa: "poke" encuentra "poké" Y "poké" encuentra "poke"
            query = query.Where(g =>
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchLower}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchNoAccents}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchWithE}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchWithO}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchWithA}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchWithI}%") ||
                EF.Functions.Like(EF.Functions.Collate(g.Name, "NOCASE"), $"%{searchWithU}%") ||
                (g.Comment != null && (
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchLower}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchNoAccents}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchWithE}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchWithO}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchWithA}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchWithI}%") ||
                    EF.Functions.Like(EF.Functions.Collate(g.Comment, "NOCASE"), $"%{searchWithU}%")
                )));
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

        if (parameters.IsCheaperByKey.HasValue)
        {
            query = query.Where(g => g.IsCheaperByKey == parameters.IsCheaperByKey.Value);
        }

        if (parameters.ShowIncomplete == true)
        {
            query = query.Where(g =>
                g.Status != null && g.Status.Name == "Not Fulfilled" ||
                string.IsNullOrEmpty(g.Cover) ||
                string.IsNullOrEmpty(g.Logo) ||
                g.PlatformId == null
            );
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
                "status" => parameters.SortDescending ? query.OrderByDescending(g => g.Status.SortOrder).ThenByDescending(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Status.SortOrder).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "score" => parameters.SortDescending ? query.OrderByDescending(g => (double?)g.Score).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => (double?)g.Score).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "released" => parameters.SortDescending ? query.OrderByDescending(g => g.Released).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Released).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "started" => parameters.SortDescending ? query.OrderByDescending(g => g.Started).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Started).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "finished" => parameters.SortDescending ? query.OrderByDescending(g => g.Finished).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.Finished).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "createdat" or "created" => parameters.SortDescending ? query.OrderByDescending(g => g.CreatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.CreatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "updatedat" or "updated" or "lastedited" => parameters.SortDescending ? query.OrderByDescending(g => g.UpdatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) : query.OrderBy(g => g.UpdatedAt).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")),
                "creation" or "id" => parameters.SortDescending ? query.OrderByDescending(g => g.Id) : query.OrderBy(g => g.Id),
                _ => query.OrderBy(g => g.Status.SortOrder).ThenBy(g => EF.Functions.Collate(g.Status.Name, "NOCASE")).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")) // Default: ordenar por status ascendente
            };
        }
        else
        {
            query = query.OrderBy(g => g.Status.SortOrder).ThenBy(g => EF.Functions.Collate(g.Status.Name, "NOCASE")).ThenBy(g => EF.Functions.Collate(g.Name, "NOCASE")); // Default: ordenar por status ascendente
        }

        return query;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<GameDto>> GetGame(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        var game = await _context.Games
            .Where(g => g.UserId == userId)
            .Include(g => g.Status)
            .Include(g => g.Platform)
            .Include(g => g.GamePlayWiths)
                .ThenInclude(gpw => gpw.PlayWith)
            .Include(g => g.PlayedStatus)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (game == null)
            return NotFound();

        return Ok(game.ToDto());
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> PutGame(int id, [FromBody] System.Text.Json.JsonElement gameDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        var game = await _context.Games
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);

        if (game == null)
            return NotFound();

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
            var logoUrl = logoElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : logoElement.GetString();
            // Validate URL if provided
            if (!string.IsNullOrWhiteSpace(logoUrl) && !IsValidUrl(logoUrl))
            {
                return BadRequest(new { error = "Invalid logo URL format" });
            }
            game.Logo = logoUrl;
        }

        if (gameDto.TryGetProperty("cover", out var coverElement))
        {
            var coverUrl = coverElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : coverElement.GetString();
            // Validate URL if provided
            if (!string.IsNullOrWhiteSpace(coverUrl) && !IsValidUrl(coverUrl))
            {
                return BadRequest(new { error = "Invalid cover URL format" });
            }
            game.Cover = coverUrl;
        }

        if (gameDto.TryGetProperty("isCheaperByKey", out var isCheaperByKeyElement))
        {
            game.IsCheaperByKey = isCheaperByKeyElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : isCheaperByKeyElement.GetBoolean();
        }

        if (gameDto.TryGetProperty("keyStoreUrl", out var keyStoreUrlElement))
        {
            var keyStoreUrl = keyStoreUrlElement.ValueKind == System.Text.Json.JsonValueKind.Null ? null : keyStoreUrlElement.GetString();
            // Validate URL if provided
            if (!string.IsNullOrWhiteSpace(keyStoreUrl) && !IsValidUrl(keyStoreUrl))
            {
                return BadRequest(new { error = "Invalid key store URL format" });
            }
            game.KeyStoreUrl = keyStoreUrl;
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

    [HttpPost]
    public async Task<ActionResult<GameDto>> PostGame(GameCreateDto gameDto)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        var statusExists = await _context.GameStatuses
            .AnyAsync(s => s.Id == gameDto.StatusId && s.UserId == userId);
        if (!statusExists)
            return BadRequest(new { message = "Invalid StatusId for current user" });

        if (gameDto.PlatformId.HasValue)
        {
            var platformExists = await _context.GamePlatforms
                .AnyAsync(p => p.Id == gameDto.PlatformId.Value && p.UserId == userId);
            if (!platformExists)
                return BadRequest(new { message = "Invalid PlatformId for current user" });
        }

        if (gameDto.PlayedStatusId.HasValue)
        {
            var playedStatusExists = await _context.GamePlayedStatuses
                .AnyAsync(ps => ps.Id == gameDto.PlayedStatusId.Value && ps.UserId == userId);
            if (!playedStatusExists)
                return BadRequest(new { message = "Invalid PlayedStatusId for current user" });
        }

        var game = gameDto.ToEntity();
        game.UserId = userId;
        _context.Games.Add(game);
        await _context.SaveChangesAsync();

        if (gameDto.PlayWithIds != null && gameDto.PlayWithIds.Any())
        {
            var validPlayWithIds = await _context.GamePlayWiths
                .Where(pw => gameDto.PlayWithIds.Contains(pw.Id) && pw.UserId == userId)
                .Select(pw => pw.Id)
                .ToListAsync();

            if (validPlayWithIds.Count != gameDto.PlayWithIds.Count)
                return BadRequest(new { message = "One or more PlayWithIds are invalid for current user" });

            foreach (var playWithId in validPlayWithIds)
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

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGame(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        var game = await _context.Games
            .FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);

        if (game == null)
            return NotFound();

        _context.Games.Remove(game);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    private bool GameExists(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        return _context.Games.Any(e => e.Id == id && e.UserId == userId);
    }

    /// <summary>
    /// Validates if a string is a valid URL
    /// </summary>
    private static bool IsValidUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Check if it's a valid URI
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
            return false;

        // Must be http or https
        return uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps;
    }

    /// <summary>
    /// Normaliza un string removiendo tildes y diacríticos.
    /// Pokémon -> Pokemon, José -> Jose, etc.
    /// </summary>
    private static string RemoveDiacritics(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var normalizedString = text.Normalize(System.Text.NormalizationForm.FormD);
        var stringBuilder = new System.Text.StringBuilder();

        foreach (var c in normalizedString)
        {
            var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                stringBuilder.Append(c);
            }
        }

        return stringBuilder.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    [HttpPatch("bulk")]
    public async Task<ActionResult<BulkUpdateResult>> BulkUpdateGames([FromBody] BulkUpdateGameDto bulkUpdate)
    {
        var userId = GetCurrentUserIdOrDefault(1);

        if (bulkUpdate.GameIds == null || bulkUpdate.GameIds.Length == 0)
        {
            return BadRequest("No game IDs provided");
        }

        var games = await _context.Games
            .Where(g => bulkUpdate.GameIds.Contains(g.Id) && g.UserId == userId)
            .ToListAsync();

        if (games.Count == 0)
        {
            return NotFound("No games found with provided IDs");
        }

        int updatedCount = 0;

        foreach (var game in games)
        {
            bool updated = false;

            if (bulkUpdate.StatusId.HasValue)
            {
                game.StatusId = bulkUpdate.StatusId.Value;
                updated = true;
            }

            if (bulkUpdate.PlatformId.HasValue)
            {
                game.PlatformId = bulkUpdate.PlatformId.Value;
                updated = true;
            }

            if (bulkUpdate.PlayedStatusId.HasValue)
            {
                game.PlayedStatusId = bulkUpdate.PlayedStatusId.Value;
                updated = true;
            }

            if (bulkUpdate.IsCheaperByKey.HasValue)
            {
                game.IsCheaperByKey = bulkUpdate.IsCheaperByKey.Value;
                updated = true;
            }

            if (bulkUpdate.PlayWithIds != null)
            {
                // Remove existing PlayWith mappings
                var existingMappings = await _context.GamePlayWithMappings
                    .Where(gpw => gpw.GameId == game.Id)
                    .ToListAsync();
                _context.GamePlayWithMappings.RemoveRange(existingMappings);

                // Add new PlayWith mappings
                foreach (var playWithId in bulkUpdate.PlayWithIds)
                {
                    _context.GamePlayWithMappings.Add(new Models.GamePlayWithMapping
                    {
                        GameId = game.Id,
                        PlayWithId = playWithId
                    });
                }
                updated = true;
            }

            if (updated)
            {
                game.UpdatedAt = DateTime.UtcNow;
                updatedCount++;
            }
        }

        await _context.SaveChangesAsync();

        return Ok(new BulkUpdateResult
        {
            UpdatedCount = updatedCount,
            TotalRequested = bulkUpdate.GameIds.Length
        });
    }
}

public class BulkUpdateResult
{
    public int UpdatedCount { get; set; }
    public int TotalRequested { get; set; }
}