using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Data;
using GamesDatabase.Api.DTOs;
using GamesDatabase.Api.Helpers;
using GamesDatabase.Api.Models;
using System.Text.Json;

namespace GamesDatabase.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GameViewsController : BaseApiController
{
    private readonly GamesDbContext _context;

    public GameViewsController(GamesDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Obtiene todas las vistas de juegos con resumen
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GameViewSummaryDto>>> GetGameViews([FromQuery] bool includePrivate = false)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var query = _context.GameViews.Where(v => v.UserId == userId);

        if (!includePrivate)
        {
            query = query.Where(v => v.IsPublic);
        }

        var views = await query
            .OrderBy(v => v.Name)
            .ToListAsync();

        var viewDtos = views.Select(v => v.ToSummaryDto()).ToList();

        return Ok(viewDtos);
    }

    /// <summary>
    /// Obtiene una vista específica por ID con configuración completa
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<GameViewDto>> GetGameView(int id)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var view = await _context.GameViews
            .FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);

        if (view == null)
        {
            return NotFound($"Vista con ID {id} no encontrada.");
        }

        return Ok(view.ToDto());
    }

    /// <summary>
    /// Obtiene una vista específica por nombre con configuración completa
    /// </summary>
    [HttpGet("by-name/{name}")]
    public async Task<ActionResult<GameViewDto>> GetGameViewByName(string name)
    {
        var userId = GetCurrentUserIdOrDefault(1);
        var view = await _context.GameViews
            .FirstOrDefaultAsync(v => v.Name == name && v.UserId == userId);

        if (view == null)
        {
            return NotFound($"Vista con nombre '{name}' no encontrada.");
        }

        return Ok(view.ToDto());
    }

    /// <summary>
    /// Crea una nueva vista de juegos
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<GameViewDto>> CreateGameView(GameViewCreateDto createDto)
    {
        try
        {
            var userId = GetCurrentUserIdOrDefault(1);

            // Verificar que el nombre no esté duplicado para este usuario
            var existingView = await _context.GameViews
                .FirstOrDefaultAsync(v => v.Name == createDto.Name && v.UserId == userId);

            if (existingView != null)
            {
                return BadRequest($"Ya existe una vista con el nombre '{createDto.Name}'.");
            }

            // Validar configuración
            var validationResult = ValidateViewConfiguration(createDto.Configuration);
            if (!validationResult.IsValid)
            {
                return BadRequest(validationResult.ErrorMessage);
            }

            var view = createDto.ToEntity();
            view.UserId = userId;

            _context.GameViews.Add(view);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetGameView), new { id = view.Id }, view.ToDto());
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error interno del servidor: {ex.Message}");
        }
    }

    /// <summary>
    /// Actualiza una vista existente
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<GameViewDto>> UpdateGameView(int id, GameViewUpdateDto updateDto)
    {
        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            var view = await _context.GameViews.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
            if (view == null)
            {
                return NotFound($"Vista con ID {id} no encontrada.");
            }

            // Verificar nombre duplicado si se está cambiando
            if (!string.IsNullOrWhiteSpace(updateDto.Name) && updateDto.Name != view.Name)
            {
                var existingView = await _context.GameViews
                    .FirstOrDefaultAsync(v => v.Name == updateDto.Name && v.Id != id && v.UserId == userId);

                if (existingView != null)
                {
                    return BadRequest($"Ya existe una vista con el nombre '{updateDto.Name}'.");
                }
            }

            // Validar configuración si se está actualizando
            if (updateDto.Configuration != null)
            {
                var validationResult = ValidateViewConfiguration(updateDto.Configuration);
                if (!validationResult.IsValid)
                {
                    return BadRequest(validationResult.ErrorMessage);
                }
            }

            view.UpdateFromDto(updateDto);
            await _context.SaveChangesAsync();

            return Ok(view.ToDto());
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error interno del servidor: {ex.Message}");
        }
    }

    /// <summary>
    /// Elimina una vista
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGameView(int id)
    {
        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            var view = await _context.GameViews.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
            if (view == null)
            {
                return NotFound($"Vista con ID {id} no encontrada.");
            }

            _context.GameViews.Remove(view);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error interno del servidor: {ex.Message}");
        }
    }    /// <summary>
         /// Duplica una vista existente con un nuevo nombre
         /// </summary>
    [HttpPost("{id}/duplicate")]
    public async Task<ActionResult<GameViewDto>> DuplicateGameView(int id, [FromBody] string newName)
    {
        try
        {
            var userId = GetCurrentUserIdOrDefault(1);
            var originalView = await _context.GameViews.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
            if (originalView == null)
            {
                return NotFound($"Vista con ID {id} no encontrada.");
            }

            // Verificar que el nuevo nombre no esté duplicado para este usuario
            var existingView = await _context.GameViews
                .FirstOrDefaultAsync(v => v.Name == newName && v.UserId == userId);

            if (existingView != null)
            {
                return BadRequest($"Ya existe una vista con el nombre '{newName}'.");
            }

            var duplicatedView = new Models.GameView
            {
                UserId = userId,
                Name = newName,
                Description = originalView.Description != null ? $"Copia de {originalView.Description}" : $"Copia de {originalView.Name}",
                FiltersJson = originalView.FiltersJson,
                SortingJson = originalView.SortingJson,
                IsPublic = originalView.IsPublic,
                CreatedBy = originalView.CreatedBy
            };

            _context.GameViews.Add(duplicatedView);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetGameView), new { id = duplicatedView.Id }, duplicatedView.ToDto());
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error interno del servidor: {ex.Message}");
        }
    }

    /// <summary>
    /// Actualiza únicamente la configuración (filtros/ordenamiento) de una vista.
    /// Acepta varios formatos de body JSON:
    /// - Un objeto ViewConfiguration { filters: [...], sorting: [...] }
    /// - Un arreglo de ViewFilter (se reemplazan los filtros)
    /// - Un único ViewFilter (se reemplaza con ese único filtro)
    /// </summary>
    [HttpPut("{id}/configuration")]
    public async Task<ActionResult<GameViewDto>> UpdateGameViewConfiguration(int id, [FromBody] JsonElement body)
    {
        try
        {
            var view = await _context.GameViews.FindAsync(id);
            if (view == null)
            {
                return NotFound($"Vista con ID {id} no encontrada.");
            }

            ViewConfiguration config = new ViewConfiguration();

            // Detect shape
            if (body.ValueKind == JsonValueKind.Object)
            {
                // If it contains "filters" or "sorting" try to deserialize as full configuration
                if (body.TryGetProperty("filters", out _) || body.TryGetProperty("sorting", out _))
                {
                    try
                    {
                        config = JsonSerializer.Deserialize<ViewConfiguration>(body.GetRawText()) ?? new ViewConfiguration();
                    }
                    catch (JsonException)
                    {
                        return BadRequest("Formato de configuración inválido.");
                    }
                }
                else if (body.TryGetProperty("field", out _))
                {
                    // Single filter object
                    try
                    {
                        var single = JsonSerializer.Deserialize<ViewFilter>(body.GetRawText());
                        if (single != null)
                        {
                            // Create a filter group with this single filter
                            config.FilterGroups.Add(new FilterGroup { Filters = new List<ViewFilter> { single } });
                        }
                    }
                    catch (JsonException)
                    {
                        return BadRequest("Filtro inválido.");
                    }
                }
                else
                {
                    return BadRequest("Formato de body no reconocido. Envíe un objeto 'configuration', un filtro o un arreglo de filtros.");
                }
            }
            else if (body.ValueKind == JsonValueKind.Array)
            {
                // Array of filters
                try
                {
                    var list = JsonSerializer.Deserialize<List<ViewFilter>>(body.GetRawText());
                    if (list != null)
                    {
                        // Create a filter group with all filters
                        config.FilterGroups.Add(new FilterGroup { Filters = list });
                    }
                }
                catch (JsonException)
                {
                    return BadRequest("Array de filtros inválido.");
                }
            }
            else
            {
                return BadRequest("Formato de body no soportado.");
            }

            // Validate configuration
            var validationResult = ValidateViewConfiguration(config);
            if (!validationResult.IsValid)
            {
                return BadRequest(validationResult.ErrorMessage);
            }

            // Persist
            view.FiltersJson = JsonSerializer.Serialize(config);
            view.SortingJson = config.Sorting.Any() ? JsonSerializer.Serialize(config.Sorting) : null;
            await _context.SaveChangesAsync();

            return Ok(view.ToDto());
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error interno del servidor: {ex.Message}");
        }
    }

    /// <summary>
    /// Valida la configuración de una vista
    /// </summary>
    private ValidationResult ValidateViewConfiguration(Models.ViewConfiguration configuration)
    {
        try
        {
            // Validar grupos de filtros
            foreach (var group in configuration.FilterGroups)
            {
                foreach (var filter in group.Filters)
                {
                    if (!Enum.IsDefined(typeof(Models.FilterField), filter.Field))
                    {
                        return new ValidationResult(false, $"Campo de filtro inválido: {filter.Field}");
                    }

                    if (!Enum.IsDefined(typeof(Models.FilterOperator), filter.Operator))
                    {
                        return new ValidationResult(false, $"Operador de filtro inválido: {filter.Operator}");
                    }

                    // Validar que operadores que requieren valores los tengan
                    var operatorsRequiringValue = new[]
                    {
                        Models.FilterOperator.Equals, Models.FilterOperator.NotEquals,
                        Models.FilterOperator.Contains, Models.FilterOperator.NotContains,
                        Models.FilterOperator.GreaterThan, Models.FilterOperator.GreaterThanOrEqual,
                        Models.FilterOperator.LessThan, Models.FilterOperator.LessThanOrEqual,
                        Models.FilterOperator.In, Models.FilterOperator.NotIn,
                        Models.FilterOperator.StartsWith, Models.FilterOperator.EndsWith
                    };

                    if (operatorsRequiringValue.Contains(filter.Operator) && filter.Value == null)
                    {
                        return new ValidationResult(false, $"El operador {filter.Operator} requiere un valor.");
                    }

                    if (filter.Operator == Models.FilterOperator.Between && (filter.Value == null || filter.SecondValue == null))
                    {
                        return new ValidationResult(false, "El operador Between requiere dos valores.");
                    }
                }
            }

            // Validar ordenamientos
            foreach (var sort in configuration.Sorting)
            {
                if (!Enum.IsDefined(typeof(Models.SortField), sort.Field))
                {
                    return new ValidationResult(false, $"Campo de ordenamiento inválido: {sort.Field}");
                }

                if (!Enum.IsDefined(typeof(Models.SortDirection), sort.Direction))
                {
                    return new ValidationResult(false, $"Dirección de ordenamiento inválida: {sort.Direction}");
                }
            }

            return new ValidationResult(true, null);
        }
        catch (Exception ex)
        {
            return new ValidationResult(false, $"Error validando configuración: {ex.Message}");
        }
    }

    private class ValidationResult
    {
        public bool IsValid { get; }
        public string? ErrorMessage { get; }

        public ValidationResult(bool isValid, string? errorMessage)
        {
            IsValid = isValid;
            ErrorMessage = errorMessage;
        }
    }
}