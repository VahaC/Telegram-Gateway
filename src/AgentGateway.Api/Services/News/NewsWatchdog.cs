using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AgentGateway.Api.Contracts;
using AgentGateway.Api.Services.Delivery;
using AgentGateway.Core.Options;
using AgentGateway.Core.Scheduling;
using AgentGateway.Infrastructure.Persistence;

namespace AgentGateway.Api.Services.News;

/// <summary>
/// Tells the owner on Telegram when the daily issue did not arrive. At News:WatchdogTime in News:WatchdogTimeZone it looks
/// for an issue of that moment's issue date (Europe/Kyiv) in any status; a missing one gets one alert through the
/// ordinary message outbox. The delivery's idempotency key is the date, so repeats and restarts never alert twice, and a
/// start after the check time catches up on the latest check.
/// </summary>
public sealed class NewsWatchdog(ApplicationDbContext database, DeliveryService deliveries, IOptions<NewsOptions> options,
    TimeProvider time, ILogger<NewsWatchdog> logger)
{
    public const string RoutineName = "Daily tech news digest";

    /// <summary>Returns true when this call queued the alert.</summary>
    public async Task<bool> CheckOnceAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !settings.WatchdogEnabled) return false;
        // Both values are checked at startup; a stored value that still fails falls back instead of stopping the loop.
        var checkTime = TimeOnly.TryParseExact(settings.WatchdogTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed : new TimeOnly(6, 0);
        var dueUtc = DailySchedule.LatestDueUtc(time.GetUtcNow(), checkTime, DailySchedule.ResolveZone(settings.WatchdogTimeZone));
        var date = NewsCalendar.DateAt(dueUtc);
        if (await database.NewsIssues.AnyAsync(issue => issue.Date == date, cancellationToken)) return false;
        var key = $"news-watchdog-{date:yyyy-MM-dd}";
        if (await database.Deliveries.AnyAsync(delivery => delivery.IdempotencyKey == key, cancellationToken)) return false;
        // Earlier dates only: the live check (scripts/check-news.sh) parks its fixture a year ahead.
        var latest = await database.NewsIssues.Where(issue => issue.Date < date).MaxAsync(issue => (DateOnly?)issue.Date, cancellationToken);
        var text = $"⚠️ Техдайджест за {Day(date)} не надійшов: о {checkTime:HH\\:mm} {settings.WatchdogTimeZone} випуску в шлюзі немає. "
            + $"Останній випуск: {(latest is { } day ? Day(day) : "ще не було")}. Перевір запуск routine «{RoutineName}» у claude.ai → Code → Routines. "
            + "Докладніше: README → News watchdog.";
        var result = await deliveries.SubmitMessageAsync(new MessageRequest(text, "plain", false, key), cancellationToken);
        if (result.Delivery is not { } delivery)
        {
            logger.LogWarning("News watchdog alert for {Date} was not queued ({Status}).", date, result.Status);
            return false;
        }
        logger.LogWarning("News watchdog: no issue for {Date} at the {CheckTime} {TimeZone} check; Telegram alert {DeliveryId} queued.",
            date, settings.WatchdogTime, settings.WatchdogTimeZone, delivery.Id);
        return true;
    }

    private static string Day(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
}
