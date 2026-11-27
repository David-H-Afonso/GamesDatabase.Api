using GamesDatabase.Api.Contracts;

namespace GamesDatabase.Api.Common;

/// <summary>Cosmetic title matching never overrides a known year or platform conflict.</summary>
public static class MetadataEditionMatching
{
    public static int? Year(string? date) => date is { Length: >= 4 } && int.TryParse(date[..4], out var year) && year is >= 1950 and <= 2200 ? year : null;
    public static bool SameTitle(string left, string right) => MetadataNameMatching.Normalize(left) is { Length: > 0 } normalized && normalized == MetadataNameMatching.Normalize(right);

    public static string Platform(string value) => MetadataNameMatching.Normalize(value) switch
    {
        "switch 2" or "nintendo switch 2" => "switch2",
        "switch" or "nintendo switch" => "switch",
        "n64" or "nintendo 64" => "n64",
        "3ds" or "nintendo 3ds" => "3ds",
        "pc" or "steam" or "windows" or "microsoft windows" => "pc",
        "ps5" or "playstation 5" or "sony playstation 5" => "ps5",
        "ps4" or "playstation 4" or "sony playstation 4" => "ps4",
        "xbox series x" or "xbox series s" or "xbox series x s" or "xbox series" or "microsoft xbox series x s" => "xboxseries",
        "xbox one" => "xboxone",
        var normalized => normalized
    };

    public static bool Conflicts(GameMetadataIdentityDto left, GameMetadataIdentityDto right)
    {
        // A shared verified provider ID is stronger evidence than differing
        // regional/port release years. A different ID is never cosmetic.
        if (left.SteamAppId.HasValue && right.SteamAppId.HasValue) return left.SteamAppId != right.SteamAppId;
        if (left.WikidataId is not null && right.WikidataId is not null) return left.WikidataId != right.WikidataId;
        return left.ReleaseYear.HasValue && right.ReleaseYear.HasValue && left.ReleaseYear != right.ReleaseYear
            || left.Platforms.Count > 0 && right.Platforms.Count > 0 && !left.Platforms.Select(Platform).Intersect(right.Platforms.Select(Platform), StringComparer.Ordinal).Any();
    }

    public static T? Best<T>(IEnumerable<T> candidates, string name, Func<T, GameMetadataIdentityDto> identity, GameMetadataIdentityDto context) where T : class =>
        MetadataNameMatching.Best(candidates.Where(candidate => !Conflicts(identity(candidate), context)), name, candidate => identity(candidate).Name);
}
