using System.Globalization;
using System.Text.RegularExpressions;

namespace GamesDatabase.Api.Common;

public static partial class GameDateNormalizer
{
    private static readonly string[] SteamReleaseDateFormats =
    [
        "d MMM, yyyy",
        "dd MMM, yyyy",
        "MMM d, yyyy",
        "MMM dd, yyyy",
        "d MMM yyyy",
        "dd MMM yyyy",
        "MMM d yyyy",
        "MMM dd yyyy",
        "d MMMM, yyyy",
        "dd MMMM, yyyy",
        "MMMM d, yyyy",
        "MMMM dd, yyyy",
        "d MMMM yyyy",
        "dd MMMM yyyy",
        "MMMM d yyyy",
        "MMMM dd yyyy"
    ];

    public static string? NormalizeSteamReleaseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var normalized = WhitespaceRegex().Replace(value.Trim(), " ");

        if (DateOnly.TryParseExact(normalized, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var isoDate))
            return isoDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        if (DateOnly.TryParseExact(normalized, SteamReleaseDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var steamDate))
            return steamDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return null;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
