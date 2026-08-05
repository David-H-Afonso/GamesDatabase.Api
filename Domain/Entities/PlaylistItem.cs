using System.Text.Json.Serialization;

namespace GamesDatabase.Api.Domain.Entities;

public class PlaylistItem
{
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public int GameId { get; set; }
    public int Position { get; set; }

    [JsonIgnore]
    public virtual Playlist Playlist { get; set; } = null!;

    [JsonIgnore]
    public virtual Game Game { get; set; } = null!;
}
