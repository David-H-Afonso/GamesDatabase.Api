using System.ComponentModel.DataAnnotations;

namespace GamesDatabase.Api.Contracts;

public sealed record PlaylistGroupDto(int Id, string Name, int SortOrder);
public sealed class PlaylistGroupSaveDto
{
    [Required, MaxLength(150)]
    public string Name { get; set; } = string.Empty;
}
public sealed class PlaylistMoveDto
{
    public int? GroupId { get; set; }
    public List<int> OrderedIds { get; set; } = [];
}
