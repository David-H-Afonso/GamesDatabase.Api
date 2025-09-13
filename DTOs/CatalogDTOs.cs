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
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
}

public class GameStatusCreateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string Color { get; set; } = "#ffffff";
}

public class GameStatusUpdateDto
{
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string Color { get; set; } = "#ffffff";
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