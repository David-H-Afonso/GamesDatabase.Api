namespace GamesDatabase.Api.Domain.Entities;

public sealed class CollectionPreferences
{
    public int UserId { get; set; }
    public string HomeConfigJson { get; set; } = "{}";
    public string GotySelectionsJson { get; set; } = "{}";
    public int Revision { get; set; }
    public DateTime UpdatedAt { get; set; }
    public User User { get; set; } = null!;
}
