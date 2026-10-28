using System.Diagnostics.CodeAnalysis;

namespace MovieDB.Common;

/// <summary>
/// The owner's "today", derived only from the configured IANA time zone (App:TimeZone). It never depends on the
/// browser, so it is safe for validation and writes.
/// </summary>
public sealed class UserClock(TimeProvider timeProvider, TimeZoneInfo zone)
{
    public TimeZoneInfo Zone => zone;

    public DateOnly Today => TodayAt(timeProvider.GetUtcNow());

    public int CurrentYear => Today.Year;

    public DateOnly TodayAt(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, zone).DateTime);

    /// <summary>Resolves an IANA id; unknown or malformed ids are reported instead of silently using the machine zone.</summary>
    public static bool TryResolveZone(string? id, [NotNullWhen(true)] out TimeZoneInfo? zone)
    {
        zone = null;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
    }

    public static TimeZoneInfo ResolveZone(string? id) =>
        TryResolveZone(id, out var zone)
            ? zone
            : throw new InvalidOperationException($"'{id}' is not a valid time zone id.");
}
