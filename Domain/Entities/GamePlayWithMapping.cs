namespace GamesDatabase.Api.Domain.Entities;

/// <summary>
/// Tabla intermedia para la relación muchos-a-muchos entre Game y GamePlayWith
/// </summary>
public class GamePlayWithMapping
{
    public int GameId { get; set; }
    public int PlayWithId { get; set; }

    // Navigation properties
    public virtual Game Game { get; set; } = null!;
    public virtual GamePlayWith PlayWith { get; set; } = null!;
}
