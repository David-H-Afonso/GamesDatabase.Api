using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Models;

public class GameHistoryEntry
{
    public int Id { get; set; }

    /// <summary>
    /// Nullable — se pone a NULL via ON DELETE SET NULL cuando el juego se borra.
    /// GameName persiste para que el historial global siga mostrando el nombre.
    /// </summary>
    public int? GameId { get; set; }

    /// <summary>
    /// Nombre del juego en el momento de crear la entrada. Persiste aunque el juego se borre.
    /// </summary>
    public string GameName { get; set; } = string.Empty;

    public int UserId { get; set; }

    /// <summary>
    /// Campo que cambió: "Name", "Status", "Grade", "Cover", etc.
    /// </summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>
    /// Valor anterior en texto legible. Null en entradas ActionType=Created.
    /// </summary>
    public string? OldValue { get; set; }

    /// <summary>
    /// Valor nuevo en texto legible. Null en entradas ActionType=Deleted.
    /// </summary>
    public string? NewValue { get; set; }

    /// <summary>
    /// Descripción legible: "Estado cambiado de Pendiente a Completado"
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// "Created" | "Updated" | "Deleted"
    /// </summary>
    public string ActionType { get; set; } = "Updated";

    public DateTime ChangedAt { get; set; }

    [JsonIgnore]
    public virtual Game? Game { get; set; }
    [JsonIgnore]
    public virtual User User { get; set; } = null!;
}
