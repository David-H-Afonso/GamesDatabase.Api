namespace GamesDatabase.Api.Contracts;

public sealed class NameSearchDto
{
    public int Version { get; set; } = 1;
    public List<string> IncludeTerms { get; set; } = [];
    public string MatchMode { get; set; } = "any";
    public List<string> ExcludeTerms { get; set; } = [];
    public bool CaseSensitive { get; set; }
    public bool AccentSensitive { get; set; }
    public string MatchType { get; set; } = "contains";
}
