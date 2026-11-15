namespace AgentGateway.Core.Scheduling;

/// <summary>
/// A daily wall-clock time in an IANA zone. Stored as a time and a zone, not a UTC instant, so it fires at the same local
/// time all year; DST gaps and overlaps are resolved explicitly because <see cref="TimeZoneInfo.ConvertTimeToUtc(DateTime, TimeZoneInfo)"/>
/// throws on the first and silently guesses on the second.
/// </summary>
public static class DailySchedule
{
    /// <summary>The latest instant at or before <paramref name="nowUtc"/> at which the zone's clock showed <paramref name="time"/>.</summary>
    public static DateTimeOffset LatestDueUtc(DateTimeOffset nowUtc, TimeOnly time, TimeZoneInfo zone)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime);
        var due = ToUtc(today, time, zone);
        return due <= nowUtc ? due : ToUtc(today.AddDays(-1), time, zone);
    }

    /// <summary>
    /// The instant of <paramref name="time"/> on <paramref name="date"/> in <paramref name="zone"/>. A time skipped by a
    /// spring-forward gap maps to the first real instant after the gap; a time repeated by a fall-back overlap maps to the earlier one.
    /// </summary>
    public static DateTimeOffset ToUtc(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            // Gaps start and end on whole minutes and last at most a few hours, so a minute walk finds the end of the gap.
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
        }
        // In an overlap the larger offset is the first pass of the clock, i.e. the earlier instant.
        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    /// <summary>Resolves a stored zone id; an unknown id falls back to UTC so a bad value cannot stop a background loop.</summary>
    public static TimeZoneInfo ResolveZone(string? identifier)
        => !string.IsNullOrWhiteSpace(identifier) && TimeZoneInfo.TryFindSystemTimeZoneById(identifier.Trim(), out var zone) ? zone : TimeZoneInfo.Utc;
}
