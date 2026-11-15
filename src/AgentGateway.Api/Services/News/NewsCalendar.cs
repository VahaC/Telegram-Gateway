namespace AgentGateway.Api.Services.News;

/// <summary>Issue dates are Europe/Kyiv calendar days.</summary>
public static class NewsCalendar
{
    private static readonly TimeZoneInfo Kyiv = Find("Europe/Kyiv") ?? Find("Europe/Kiev") ?? TimeZoneInfo.Utc;

    public static DateOnly Today(TimeProvider time) => DateAt(time.GetUtcNow());

    /// <summary>The issue date at an instant; the watchdog asks it for its check time, which may be earlier than now.</summary>
    public static DateOnly DateAt(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Kyiv).DateTime);

    // Older ICU/tzdata builds only know the former name; UTC is off by at most one day for a history window.
    private static TimeZoneInfo? Find(string identifier)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(identifier); }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException) { return null; }
    }
}
