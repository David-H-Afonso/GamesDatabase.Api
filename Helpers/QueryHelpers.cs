namespace GamesDatabase.Api.Helpers;

public class QueryParameters
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public string? Search { get; set; }
    public string? SortBy { get; set; }
    public bool SortDescending { get; set; } = false;
    public bool? IsActive { get; set; }

    public int Skip => (Page - 1) * PageSize;
    public int Take => PageSize;
}

public class GameQueryParameters : QueryParameters
{
    public int? StatusId { get; set; }
    public int[]? ExcludeStatusIds { get; set; }
    public int? PlatformId { get; set; }
    public int? PlayWithId { get; set; }
    public int? PlayedStatusId { get; set; }
    public int? MinGrade { get; set; }
    public int? MaxGrade { get; set; }
    public string? Released { get; set; }
    public string? Started { get; set; }
    public string? Finished { get; set; }

    /// <summary>
    /// Filtrar por juegos más baratos por clave. true = solo más baratos por clave, false = solo más baratos en tienda oficial, null = todos
    /// </summary>
    public bool? IsCheaperByKey { get; set; }

    /// <summary>
    /// Filtrar juegos incompletos: sin rellenar (Not Fulfilled), sin cover, sin logo, o sin plataforma
    /// </summary>
    public bool? ShowIncomplete { get; set; }

    /// <summary>
    /// ID de la vista a aplicar. Si se especifica, se ignorarán otros filtros y ordenamientos
    /// </summary>
    public int? ViewId { get; set; }

    /// <summary>
    /// Nombre de la vista a aplicar. Si se especifica, se ignorarán otros filtros y ordenamientos
    /// </summary>
    public string? ViewName { get; set; }
}

public class PagedResult<T>
{
    public IEnumerable<T> Data { get; set; } = new List<T>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;
}