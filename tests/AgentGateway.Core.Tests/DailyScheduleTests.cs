using AgentGateway.Core.Scheduling;

namespace AgentGateway.Core.Tests;

public sealed class DailyScheduleTests
{
    private static readonly TimeZoneInfo Kyiv = TimeZoneInfo.TryFindSystemTimeZoneById("Europe/Kyiv", out var zone) ? zone
        : TimeZoneInfo.FindSystemTimeZoneById("Europe/Kiev");

    [Theory]
    [InlineData("2026-11-16", "08:00", "2026-11-16T06:00:00Z")] // EET, UTC+2
    [InlineData("2026-07-01", "08:00", "2026-07-01T05:00:00Z")] // EEST, UTC+3
    [InlineData("2026-03-29", "03:30", "2026-03-29T01:00:00Z")] // Skipped by spring-forward: the first instant after the gap (04:00 local).
    [InlineData("2026-10-25", "03:30", "2026-10-25T00:30:00Z")] // Repeated by fall-back: the earlier of the two instants.
    public void ToUtc_in_Kyiv_handles_ordinary_days_and_both_DST_changes(string date, string time, string expected)
    {
        //Arrange
        var day = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
        var clock = TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture);
        //Act
        var instant = DailySchedule.ToUtc(day, clock, Kyiv);
        //Assert
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), instant);
        Assert.Equal(TimeSpan.Zero, instant.Offset);
    }

    [Theory]
    [InlineData("2026-11-16T05:59:59Z", "2026-11-15T06:00:00Z")]
    [InlineData("2026-11-16T06:00:00Z", "2026-11-16T06:00:00Z")]
    [InlineData("2026-11-16T23:59:00Z", "2026-11-16T06:00:00Z")]
    public void LatestDueUtc_is_todays_time_once_reached_and_yesterdays_before(string now, string expected)
    {
        //Arrange
        var instant = DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture);
        //Act
        var due = DailySchedule.LatestDueUtc(instant, new TimeOnly(6, 0), TimeZoneInfo.Utc);
        //Assert
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), due);
    }

    [Fact]
    public void LatestDueUtc_on_the_day_after_spring_forward_uses_the_new_offset()
    {
        //Arrange
        var now = new DateTimeOffset(2026, 3, 30, 5, 30, 0, TimeSpan.Zero); // 08:30 in Kyiv (UTC+3 since yesterday)
        //Act
        var due = DailySchedule.LatestDueUtc(now, new TimeOnly(8, 0), Kyiv);
        //Assert
        Assert.Equal(new DateTimeOffset(2026, 3, 30, 5, 0, 0, TimeSpan.Zero), due);
    }

    [Theory]
    [InlineData("Not/A_Zone")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveZone_unknown_or_blank_falls_back_to_UTC(string? identifier)
    {
        //Arrange
        //Act
        var zone = DailySchedule.ResolveZone(identifier);
        //Assert
        Assert.Equal(TimeZoneInfo.Utc, zone);
    }
}
