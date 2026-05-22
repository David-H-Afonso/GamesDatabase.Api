using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using GamesDatabase.Api.Application.Interfaces;
using GamesDatabase.Api.Application.Mapping;
using GamesDatabase.Api.Contracts;
using GamesDatabase.Api.Domain.Entities;
using GamesDatabase.Api.Infrastructure.Persistence;

namespace GamesDatabase.Api.Application.Services;

public class GameViewService : IGameViewService
{
    private readonly GamesDbContext _context;
    private readonly ILogger<GameViewService> _logger;

    public GameViewService(GamesDbContext context, ILogger<GameViewService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IEnumerable<GameViewSummaryDto>> GetViewsAsync(int userId)
    {
        var views = await _context.GameViews
            .Where(v => v.UserId == userId)
            .OrderBy(v => v.SortOrder)
            .ThenBy(v => v.Name)
            .ToListAsync();

        return views.Select(v => v.ToSummaryDto()).ToList();
    }

    public async Task<GameViewDto?> GetViewByIdAsync(int id, int userId)
    {
        var view = await _context.GameViews
            .FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);

        return view?.ToDto();
    }

    public async Task<GameViewDto?> GetViewByNameAsync(string name, int userId)
    {
        var view = await _context.GameViews
            .FirstOrDefaultAsync(v => v.Name == name && v.UserId == userId);

        return view?.ToDto();
    }

    public async Task<CatalogServiceResult<GameViewDto>> CreateViewAsync(GameViewCreateDto createDto, int userId)
    {
        try
        {
            var existingView = await _context.GameViews
                .FirstOrDefaultAsync(v => v.Name == createDto.Name && v.UserId == userId);

            if (existingView != null)
            {
                return CatalogServiceResult<GameViewDto>.BadRequest($"Ya existe una vista con el nombre '{createDto.Name}'.");
            }

            var validationResult = ValidateViewConfiguration(createDto.Configuration);
            if (!validationResult.IsValid)
            {
                return CatalogServiceResult<GameViewDto>.BadRequest(validationResult.ErrorMessage!);
            }

            var maxSort = await _context.GameViews
                .Where(v => v.UserId == userId)
                .MaxAsync(v => (int?)v.SortOrder) ?? 0;

            var view = createDto.ToEntity();
            view.UserId = userId;
            view.SortOrder = maxSort + 1;

            _context.GameViews.Add(view);
            await _context.SaveChangesAsync();

            return CatalogServiceResult<GameViewDto>.Ok(view.ToDto());
        }
        catch (Exception ex)
        {
            return CatalogServiceResult<GameViewDto>.ServerError($"Error interno del servidor: {ex.Message}");
        }
    }

    public async Task<CatalogServiceResult<GameViewDto>> UpdateViewAsync(int id, GameViewUpdateDto updateDto, int userId)
    {
        try
        {
            var view = await _context.GameViews.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
            if (view == null)
            {
                return CatalogServiceResult<GameViewDto>.NotFoundResult($"Vista con ID {id} no encontrada.");
            }

            if (!string.IsNullOrWhiteSpace(updateDto.Name) && updateDto.Name != view.Name)
            {
                var existingView = await _context.GameViews
                    .FirstOrDefaultAsync(v => v.Name == updateDto.Name && v.Id != id && v.UserId == userId);

                if (existingView != null)
                {
                    return CatalogServiceResult<GameViewDto>.BadRequest($"Ya existe una vista con el nombre '{updateDto.Name}'.");
                }
            }

            if (updateDto.Configuration != null)
            {
                var validationResult = ValidateViewConfiguration(updateDto.Configuration);
                if (!validationResult.IsValid)
                {
                    return CatalogServiceResult<GameViewDto>.BadRequest(validationResult.ErrorMessage!);
                }
            }

            view.UpdateFromDto(updateDto);

            _context.Entry(view).Property(v => v.FiltersJson).IsModified = true;
            _context.Entry(view).Property(v => v.SortingJson).IsModified = true;

            await _context.SaveChangesAsync();

            return CatalogServiceResult<GameViewDto>.Ok(view.ToDto());
        }
        catch (Exception ex)
        {
            return CatalogServiceResult<GameViewDto>.ServerError($"Error interno del servidor: {ex.Message}");
        }
    }

    public async Task<CatalogServiceResult> ReorderViewsAsync(ReorderStatusesDto dto, int userId)
    {
        if (dto?.OrderedIds == null || dto.OrderedIds.Count == 0)
            return CatalogServiceResult.BadRequest("OrderedIds must be provided");

        var views = await _context.GameViews
            .Where(v => dto.OrderedIds.Contains(v.Id) && v.UserId == userId)
            .ToListAsync();

        if (views.Count != dto.OrderedIds.Count)
        {
            return CatalogServiceResult.NotFoundResult("One or more view IDs not found");
        }

        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            for (int i = 0; i < dto.OrderedIds.Count; i++)
            {
                var id = dto.OrderedIds[i];
                var view = views.First(v => v.Id == id);
                view.SortOrder = i + 1;
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            return CatalogServiceResult.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reordering views for user {UserId}", userId);
            await transaction.RollbackAsync();
            return CatalogServiceResult.ServerError("Error reordering views");
        }
    }

    public async Task<CatalogServiceResult> DeleteViewAsync(int id, int userId)
    {
        try
        {
            var view = await _context.GameViews.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
            if (view == null)
            {
                return CatalogServiceResult.NotFoundResult($"Vista con ID {id} no encontrada.");
            }

            _context.GameViews.Remove(view);
            await _context.SaveChangesAsync();

            return CatalogServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return CatalogServiceResult.ServerError($"Error interno del servidor: {ex.Message}");
        }
    }

    public async Task<CatalogServiceResult<GameViewDto>> DuplicateViewAsync(int id, string newName, int userId)
    {
        try
        {
            var originalView = await _context.GameViews.FirstOrDefaultAsync(v => v.Id == id && v.UserId == userId);
            if (originalView == null)
            {
                return CatalogServiceResult<GameViewDto>.NotFoundResult($"Vista con ID {id} no encontrada.");
            }

            var existingView = await _context.GameViews
                .FirstOrDefaultAsync(v => v.Name == newName && v.UserId == userId);

            if (existingView != null)
            {
                return CatalogServiceResult<GameViewDto>.BadRequest($"Ya existe una vista con el nombre '{newName}'.");
            }

            var duplicatedView = new GameView
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

            return CatalogServiceResult<GameViewDto>.Ok(duplicatedView.ToDto());
        }
        catch (Exception ex)
        {
            return CatalogServiceResult<GameViewDto>.ServerError($"Error interno del servidor: {ex.Message}");
        }
    }

    public async Task<CatalogServiceResult<GameViewDto>> UpdateViewConfigurationAsync(int id, JsonElement body, int userId)
    {
        try
        {
            var view = await _context.GameViews.FindAsync(id);
            if (view == null)
            {
                return CatalogServiceResult<GameViewDto>.NotFoundResult($"Vista con ID {id} no encontrada.");
            }

            ViewConfiguration config = new ViewConfiguration();

            if (body.ValueKind == JsonValueKind.Object)
            {
                if (body.TryGetProperty("filters", out _) || body.TryGetProperty("sorting", out _))
                {
                    try
                    {
                        config = JsonSerializer.Deserialize<ViewConfiguration>(body.GetRawText()) ?? new ViewConfiguration();
                    }
                    catch (JsonException)
                    {
                        return CatalogServiceResult<GameViewDto>.BadRequest("Formato de configuración inválido.");
                    }
                }
                else if (body.TryGetProperty("field", out _))
                {
                    try
                    {
                        var single = JsonSerializer.Deserialize<ViewFilter>(body.GetRawText());
                        if (single != null)
                        {
                            config.FilterGroups.Add(new FilterGroup { Filters = new List<ViewFilter> { single } });
                        }
                    }
                    catch (JsonException)
                    {
                        return CatalogServiceResult<GameViewDto>.BadRequest("Filtro inválido.");
                    }
                }
                else
                {
                    return CatalogServiceResult<GameViewDto>.BadRequest("Formato de body no reconocido. Envíe un objeto 'configuration', un filtro o un arreglo de filtros.");
                }
            }
            else if (body.ValueKind == JsonValueKind.Array)
            {
                try
                {
                    var list = JsonSerializer.Deserialize<List<ViewFilter>>(body.GetRawText());
                    if (list != null)
                    {
                        config.FilterGroups.Add(new FilterGroup { Filters = list });
                    }
                }
                catch (JsonException)
                {
                    return CatalogServiceResult<GameViewDto>.BadRequest("Array de filtros inválido.");
                }
            }
            else
            {
                return CatalogServiceResult<GameViewDto>.BadRequest("Formato de body no soportado.");
            }

            var validationResult = ValidateViewConfiguration(config);
            if (!validationResult.IsValid)
            {
                return CatalogServiceResult<GameViewDto>.BadRequest(validationResult.ErrorMessage!);
            }

            view.FiltersJson = JsonSerializer.Serialize(config);
            view.SortingJson = config.Sorting.Any() ? JsonSerializer.Serialize(config.Sorting) : null;
            await _context.SaveChangesAsync();

            return CatalogServiceResult<GameViewDto>.Ok(view.ToDto());
        }
        catch (Exception ex)
        {
            return CatalogServiceResult<GameViewDto>.ServerError($"Error interno del servidor: {ex.Message}");
        }
    }

    private ValidationResult ValidateViewConfiguration(ViewConfiguration configuration)
    {
        try
        {
            foreach (var group in configuration.FilterGroups)
            {
                foreach (var filter in group.Filters)
                {
                    if (!Enum.IsDefined(typeof(FilterField), filter.Field))
                    {
                        return new ValidationResult(false, $"Campo de filtro inválido: {filter.Field}");
                    }

                    if (!Enum.IsDefined(typeof(FilterOperator), filter.Operator))
                    {
                        return new ValidationResult(false, $"Operador de filtro inválido: {filter.Operator}");
                    }

                    var operatorsRequiringValue = new[]
                    {
                        FilterOperator.Equals, FilterOperator.NotEquals,
                        FilterOperator.Contains, FilterOperator.NotContains,
                        FilterOperator.GreaterThan, FilterOperator.GreaterThanOrEqual,
                        FilterOperator.LessThan, FilterOperator.LessThanOrEqual,
                        FilterOperator.In, FilterOperator.NotIn,
                        FilterOperator.StartsWith, FilterOperator.EndsWith
                    };

                    if (operatorsRequiringValue.Contains(filter.Operator) && filter.Value == null)
                    {
                        return new ValidationResult(false, $"El operador {filter.Operator} requiere un valor.");
                    }
                }
            }

            foreach (var sort in configuration.Sorting)
            {
                if (!Enum.IsDefined(typeof(SortField), sort.Field))
                {
                    return new ValidationResult(false, $"Campo de ordenamiento inválido: {sort.Field}");
                }

                if (!Enum.IsDefined(typeof(SortDirection), sort.Direction))
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
