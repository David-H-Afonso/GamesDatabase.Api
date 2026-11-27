using System.Globalization;
using System.Text;

namespace GamesDatabase.Api.Common;

/// <summary>Metadata identity matching only: cosmetic differences, not sequel/edition guessing.</summary>
public static class MetadataNameMatching
{
    public static string Normalize(string value)
    {
        var result = new StringBuilder();
        foreach (var rune in value.Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (rune.Value is 0x00AE or 0x2122 or 0x00A9) continue;
            var category = Rune.GetUnicodeCategory(rune);
            if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark) continue;
            if (Rune.IsLetterOrDigit(rune)) result.Append(Rune.ToLowerInvariant(rune));
            else if (rune.Value is not (0x0027 or 0x2019 or 0x2018)) result.Append(' ');
        }
        return string.Join(' ', result.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    public static T? Best<T>(IEnumerable<T> candidates, string name, Func<T, string> title) where T : class
    {
        var list = candidates.ToList();
        var exact = list.Where(candidate => NameSearchMatching.Matches(title(candidate), new() { IncludeTerms = [name], MatchType = "exact" })).ToList();
        if (exact.Count == 1) return exact[0];
        if (exact.Count > 1) return null;
        var normalized = Normalize(name);
        if (normalized.Length == 0) return null;
        var cosmetic = list.Where(candidate => Normalize(title(candidate)) == normalized).ToList();
        return cosmetic.Count == 1 ? cosmetic[0] : null;
    }
}
