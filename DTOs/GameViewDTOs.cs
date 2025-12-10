using GamesDatabase.Api.Models;

namespace GamesDatabase.Api.DTOs;

/// <summary>
/// DTO para mostrar una vista de juegos
/// </summary>
public class GameViewDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public ViewConfiguration Configuration { get; set; } = new();
    public bool IsPublic { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// DTO para crear una nueva vista
/// </summary>
public class GameViewCreateDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ViewConfiguration Configuration { get; set; } = new();
    public bool IsPublic { get; set; } = true;
    public string? CreatedBy { get; set; }
}

/// <summary>
/// DTO para actualizar una vista existente
/// </summary>
public class GameViewUpdateDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public ViewConfiguration? Configuration { get; set; }
    public bool? IsPublic { get; set; }
}

/// <summary>
/// DTO simplificado para listar vistas
/// </summary>
public class GameViewSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    public bool IsPublic { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int FilterCount { get; set; }
    public int SortCount { get; set; }
}

/// <summary>
/// DTO para definir un filtro en el frontend
/// </summary>
public class FilterDto
{
    public FilterField Field { get; set; }
    public FilterOperator Operator { get; set; }
    public object? Value { get; set; }
    public object? SecondValue { get; set; }
}

/// <summary>
/// DTO para definir un ordenamiento en el frontend
/// </summary>
public class SortDto
{
    public SortField Field { get; set; }
    public SortDirection Direction { get; set; }
    public int Order { get; set; }
}

/// <summary>
/// DTO para la configuración completa de una vista
/// </summary>
public class ViewConfigurationDto
{
    public List<FilterGroupDto> FilterGroups { get; set; } = new();
    public List<SortDto> Sorting { get; set; } = new();
    public Models.FilterLogic GroupCombineWith { get; set; } = Models.FilterLogic.And;
}

/// <summary>
/// DTO para un grupo de filtros
/// </summary>
public class FilterGroupDto
{
    public List<FilterDto> Filters { get; set; } = new();
    public Models.FilterLogic CombineWith { get; set; } = Models.FilterLogic.And;
}