namespace GamesDatabase.Api.DTOs;

public class GamePlatformDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
}

// DTO para crear/actualizar sin manejar manualmente el SortOrder
public class GamePlatformCreateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "#ffffff";
    // SortOrder se asigna automáticamente
}

public class GamePlatformUpdateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
    // SortOrder se mantiene, solo se puede cambiar con endpoints específicos
    // ID se toma de la URL, no del cuerpo
}

public class GameStatusDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
    public bool IsDefault { get; set; }
    public string StatusType { get; set; } = "None";
    public bool IsSpecialStatus { get; set; }
}

public class GameStatusCreateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "#ffffff";
    // Optional initial sort order. If omitted, new status will be appended to the end.
    public int? SortOrder { get; set; }
}

public class GameStatusUpdateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
    public bool? IsDefault { get; set; } // Optional for reassignment operations
    public int? SortOrder { get; set; }
}

public class SpecialStatusDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string StatusType { get; set; } = string.Empty;
    public bool IsDefault { get; set; }
    public string Color { get; set; } = "#ffffff";
}

public class ReassignDefaultStatusDto
{
    public int NewDefaultStatusId { get; set; }
    public string StatusType { get; set; } = "NotFulfilled";
}

public class ReorderStatusesDto
{
    // Ordered list of status IDs in the desired order (first = lowest SortOrder)
    public List<int> OrderedIds { get; set; } = new List<int>();
}

public class GamePlayWithDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
}

public class GamePlayWithCreateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "#ffffff";
}

public class GamePlayWithUpdateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
}

public class GamePlayedStatusDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
}

public class GamePlayedStatusCreateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "#ffffff";
}

public class GamePlayedStatusUpdateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
}