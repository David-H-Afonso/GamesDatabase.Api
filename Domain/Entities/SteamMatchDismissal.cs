using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Domain.Entities;

public class SteamMatchDismissal
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int SteamAppId { get; set; }
    public int GameId { get; set; }
    public DateTime CreatedAt { get; set; }

    [JsonIgnore]
    public virtual User User { get; set; } = null!;

    [JsonIgnore]
    public virtual Game Game { get; set; } = null!;
}
