using System.Globalization;
using System.Text;
using System.Text.Json;
using GamesDatabase.Api.Contracts;
using Microsoft.Data.Sqlite;

namespace GamesDatabase.Api.Common;

/// <summary>Literal Unicode search shared by the SQL query paths and rule previews.</summary>
public static class NameSearchMatching
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? Validate(NameSearchDto? search)
    {
        if (search is null) return null;
        if (search.Version != 1 || search.MatchMode is not ("any" or "all") || search.MatchType is not ("contains" or "exact")) return "Unsupported name-search contract.";
        if (search.IncludeTerms is null || search.ExcludeTerms is null || search.IncludeTerms.Count > 20 || search.ExcludeTerms.Count > 20) return "Use at most 20 included and 20 excluded terms.";
        if (search.IncludeTerms.Concat(search.ExcludeTerms).Any(term => term is null || term.Length > 250)) return "Search phrases must contain at most 250 characters.";
        return null;
    }

    public static NameSearchDto Read(string json)
    {
        if (json.Length > 16_000) throw new ArgumentException("Name-search configuration is too large.");
        var value = JsonSerializer.Deserialize<NameSearchDto>(json, JsonOptions) ?? throw new ArgumentException("Name search cannot be null.");
        var error = Validate(value);
        if (error is not null) throw new ArgumentException(error);
        return value;
    }

    public static string Serialize(NameSearchDto search) => JsonSerializer.Serialize(search, JsonOptions);

    public static string Normalize(string? value, bool caseSensitive = false, bool accentSensitive = false)
    {
        var normalized = (value ?? string.Empty).Normalize(NormalizationForm.FormC);
        if (!accentSensitive)
        {
            var builder = new StringBuilder();
            foreach (var rune in normalized.Normalize(NormalizationForm.FormD).EnumerateRunes())
            {
                var category = Rune.GetUnicodeCategory(rune);
                if (category is not (UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)) builder.Append(rune);
            }
            normalized = builder.ToString();
        }
        return caseSensitive ? normalized : normalized.Replace("\u0130", "i\u0307", StringComparison.Ordinal).ToLowerInvariant();
    }

    public static bool Matches(string? value, NameSearchDto search)
    {
        var normalized = Normalize(value, search.CaseSensitive, search.AccentSensitive);
        bool Hit(string term)
        {
            var phrase = Normalize(term.Trim(), search.CaseSensitive, search.AccentSensitive);
            return search.MatchType == "exact" ? normalized == phrase : normalized.Contains(phrase, StringComparison.Ordinal);
        }
        var included = search.IncludeTerms.Where(term => !string.IsNullOrWhiteSpace(term)).Distinct(StringComparer.Ordinal).ToList();
        if (search.ExcludeTerms.Where(term => !string.IsNullOrWhiteSpace(term)).Any(Hit)) return false;
        return included.Count == 0 || (search.MatchMode == "all" ? included.All(Hit) : included.Any(Hit));
    }

    // Translated by EF; never evaluated as client-side filtering of whole collections.
    public static bool SqlMatch(string? value, string json) => Matches(value, Read(json));

    public static void Register(SqliteConnection connection)
    {
        var parsed = new Dictionary<string, NameSearchDto>(StringComparer.Ordinal);
        connection.CreateFunction<string?, string, bool>("gdb_name_match", (value, json) =>
        {
            if (!parsed.TryGetValue(json, out var search))
            {
                if (parsed.Count >= 32) parsed.Clear();
                parsed[json] = search = Read(json);
            }
            return Matches(value, search);
        }, isDeterministic: true);
    }
}
