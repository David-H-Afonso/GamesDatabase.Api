namespace GamesDatabase.Api.Domain.Entities;

public sealed class PlaylistGroup
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
