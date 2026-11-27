using System.Globalization;

namespace GamesDatabase.Api.Common;

/// <summary>Steam activity timestamps are evidence, not guaranteed start/story-finish dates.</summary>
public static class SteamDateEvidence
{
    private static readonly DateTime Earliest = new(2003, 9, 11, 0, 0, 0, DateTimeKind.Utc);
    public static DateTime Utc(DateTime value) => value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    public static bool IsPlausible(DateTime value) => Utc(value) >= Earliest && Utc(value) <= DateTime.UtcNow;
    public static DateTime? FromUnix(long? seconds)
    {
        if (seconds is not (> 0 and <= 253402300799)) return null;
        var date = DateTimeOffset.FromUnixTimeSeconds(seconds.Value).UtcDateTime;
        return IsPlausible(date) ? date : null;
    }
    public static string? Day(DateTime? value, TimeZoneInfo? zone = null) => value.HasValue && IsPlausible(value.Value)
        ? TimeZoneInfo.ConvertTimeFromUtc(Utc(value.Value), zone ?? TimeZoneInfo.Utc).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    public static bool TryDay(string? value, out DateOnly day, TimeZoneInfo? zone = null) => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day)
        && day >= DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(Earliest, zone ?? TimeZoneInfo.Utc))
        && day <= DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, zone ?? TimeZoneInfo.Utc));
}
