using System.Globalization;
using Microsoft.Data.Sqlite;

namespace GamesDatabase.Api.Common;

/// <summary>Calendar-date release rules, independent of stored time or the server's timezone.</summary>
public static class ReleaseDateMatching
{
    public static bool OnOrAfter(string? released, string today)
    {
        if (released is null || released.Length < 10 || released.Length > 10 && released[10] is not ('T' or ' ')) return false;
        return DateOnly.TryParseExact(released[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            && DateOnly.TryParseExact(today, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var minimum)
            && date >= minimum;
    }

    public static TimeZoneInfo? FindTimeZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 100) return null;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }

    public static string Today(TimeZoneInfo zone, DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static void Register(SqliteConnection connection) =>
        connection.CreateFunction<string?, string, bool>("gdb_release_on_or_after", OnOrAfter, isDeterministic: true);
}
