using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Models;

/// <summary>
/// Representa una vista personalizada de juegos con filtros y ordenamientos específicos
/// </summary>
public class GameView
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Configuración de filtros en formato JSON
    /// </summary>
    [Required]
    public string FiltersJson { get; set; } = string.Empty;

    /// <summary>
    /// Configuración de ordenamientos en formato JSON
    /// </summary>
    public string? SortingJson { get; set; }

    /// <summary>
    /// Orden de visualización de la vista (1-based)
    /// </summary>
    public int SortOrder { get; set; } = 999;

    public bool IsPublic { get; set; } = true;

    [MaxLength(50)]
    public string? CreatedBy { get; set; }

    public int UserId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Flag to indicate if the view has been modified since last export
    /// </summary>
    public bool ModifiedSinceExport { get; set; } = true;

    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}

/// <summary>
/// Tipo de operador para filtros
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FilterOperator
{
    Equals,
    NotEquals,
    Contains,
    NotContains,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
    In,
    NotIn,
    IsNull,
    IsNotNull,
    StartsWith,
    EndsWith,
    // Operadores específicos para fechas
    On,        // Fecha exacta (igual que Equals pero más semántico)
    Before,    // Antes de esta fecha (incluyendo la fecha) - equivale a LessThanOrEqual
    After      // Después de esta fecha (incluyendo la fecha) - equivale a GreaterThanOrEqual
}

/// <summary>
/// Tipo de campo para filtros
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FilterField
{
    Name,
    StatusId,
    PlatformId,
    PlayWithId,
    PlayedStatusId,
    Grade,
    Critic,
    Story,
    Completion,
    Score,
    Released,
    Started,
    Finished,
    Comment,
    CriticProvider,
    CreatedAt,
    UpdatedAt
}

/// <summary>
/// Campo para ordenamiento
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortField
{
    Name,
    StatusId,
    Status,
    PlatformId,
    Platform,
    PlayWithId,
    PlayWith,
    PlayedStatusId,
    PlayedStatus,
    Grade,
    Critic,
    Story,
    Completion,
    Score,
    Released,
    Started,
    Finished,
    CriticProvider,
    CreatedAt,
    UpdatedAt,
    Id
}

/// <summary>
/// Dirección de ordenamiento
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortDirection
{
    Ascending,
    Descending
}

/// <summary>
/// Definición de un filtro individual
/// </summary>
public class ViewFilter
{
    public FilterField Field { get; set; }
    public FilterOperator Operator { get; set; }
    public object? Value { get; set; }
    public object? SecondValue { get; set; } // Para operadores como Between
}

/// <summary>
/// Definición de un ordenamiento individual
/// </summary>
public class ViewSort
{
    public SortField Field { get; set; }
    public SortDirection Direction { get; set; }
    public int Order { get; set; } // Para múltiples ordenamientos
}

/// <summary>
/// Configuración completa de filtros para una vista
/// </summary>
public class ViewConfiguration
{
    public List<FilterGroup> FilterGroups { get; set; } = new();
    public List<ViewSort> Sorting { get; set; } = new();

    /// <summary>
    /// Indica cómo combinar los grupos de filtros: AND (por defecto) o OR
    /// </summary>
    public FilterLogic GroupCombineWith { get; set; } = FilterLogic.And;
}

/// <summary>
/// Grupo de filtros que se evalúan juntos
/// </summary>
public class FilterGroup
{
    public List<ViewFilter> Filters { get; set; } = new();

    /// <summary>
    /// Indica cómo combinar los filtros dentro de este grupo: AND (por defecto) o OR
    /// </summary>
    public FilterLogic CombineWith { get; set; } = FilterLogic.And;
}

/// <summary>
/// Operador lógico para combinar filtros
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FilterLogic
{
    And,
    Or
}