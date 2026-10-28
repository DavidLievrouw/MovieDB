using System.Diagnostics.CodeAnalysis;

namespace MovieDB.Common;

/// <summary>
/// The owner's "today", derived only from the configured IANA time zone (App:TimeZone). It never depends on the
/// browser, so it is safe for validation and writes.
/// </summary>
public sealed class UserClock
{
    private readonly TimeProvider _timeProvider;

    public UserClock(TimeProvider timeProvider, TimeZoneInfo zone)
    {
        _timeProvider = timeProvider;
        Zone = zone;
    }

    public TimeZoneInfo Zone { get; }

    public DateOnly Today => TodayAt(_timeProvider.GetUtcNow());

    public int CurrentYear => Today.Year;

    public DateOnly TodayAt(DateTimeOffset utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(utcNow, Zone).DateTime);

    /// <summary>Resolves an IANA id; unknown or malformed ids are reported instead of silently using the machine zone.</summary>
    /// <param name="id">The IANA time zone id.</param>
    /// <param name="zone">The resolved time zone when the method returns true.</param>
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
