using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Domain.Entities;

public class DuplicateGameDismissal
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int GameIdA { get; set; }
    public int GameIdB { get; set; }
    public DateTime CreatedAt { get; set; }

    [JsonIgnore]
    public virtual User User { get; set; } = null!;

    [JsonIgnore]
    public virtual Game GameA { get; set; } = null!;

    [JsonIgnore]
    public virtual Game GameB { get; set; } = null!;
}
