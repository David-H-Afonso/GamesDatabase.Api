using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GamesDatabase.Api.Helpers;

/// <summary>
/// Helper class for generating safe folder names from game names.
/// This ensures consistency between image URL generation and network sync operations.
/// </summary>
public static class FolderNameHelper
{
    /// <summary>
    /// Converts a game name to a safe folder name by:
    /// - Removing accents and diacritics (é→e, ó→o, á→a)
    /// - Keeping only: letters, digits, space, hyphen, underscore, dot, parentheses
    /// - Removing symbols like apostrophes, commas, ™, ®, etc.
    /// - Replacing spaces with underscores
    /// - Removing consecutive underscores
    /// - Limiting length to 200 characters for Windows compatibility
    /// </summary>
    /// <param name="name">The game name to convert</param>
    /// <returns>A safe folder name string</returns>
    public static string MakeSafeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;

        // Normalize to remove accents/diacritics (é→e, ó→o, á→a, etc.)
        var normalized = name.Normalize(NormalizationForm.FormD);
        var stringBuilder = new StringBuilder();

        foreach (var c in normalized)
        {
            // Skip diacritical marks (accent marks)
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue; // Skip accent marks
            }

            // Only allow: letters, digits, space, hyphen, underscore, dot, parentheses
            // Everything else (apostrophes, commas, symbols like ™®, etc.) is removed
            if (char.IsLetterOrDigit(c) || c == ' ' || c == '-' || c == '_' || c == '.' || c == '(' || c == ')')
            {
                stringBuilder.Append(c);
            }
        }

        var safeName = stringBuilder.ToString();

        // Remove any remaining invalid filename characters (but keep underscores)
        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var invalidChar in invalidChars)
        {
            if (invalidChar != '_') // Don't replace underscores
            {
                safeName = safeName.Replace(invalidChar, '_');
            }
        }

        // Replace spaces with underscores
        safeName = safeName.Replace(" ", "_");

        // Remove multiple consecutive underscores
        safeName = Regex.Replace(safeName, "_+", "_");

        // Remove leading/trailing underscores and dots
        safeName = safeName.Trim('_', '.');

        // Limit length to avoid Windows MAX_PATH issues
        if (safeName.Length > 200)
        {
            safeName = safeName.Substring(0, 200).TrimEnd('_', '.');
        }

        return safeName;
    }
}
