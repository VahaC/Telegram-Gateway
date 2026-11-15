using System.Globalization;
using System.Text.RegularExpressions;

namespace AgentGateway.Infrastructure.Feeds;

/// <summary>
/// Feed dates: RSS uses RFC 822 (often with a two-digit year, no seconds or a zone abbreviation), Atom and Dublin Core
/// use ISO 8601. .NET parses neither the zone abbreviations nor every RFC 822 variant, so that form is read by hand.
/// </summary>
internal static partial class NewsFeedDates
{
    private static readonly string[] Months = ["jan", "feb", "mar", "apr", "may", "jun", "jul", "aug", "sep", "oct", "nov", "dec"];

    private static readonly Dictionary<string, int> ZoneHours = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GMT"] = 0, ["UT"] = 0, ["UTC"] = 0, ["Z"] = 0,
        ["EST"] = -5, ["EDT"] = -4, ["CST"] = -6, ["CDT"] = -5, ["MST"] = -7, ["MDT"] = -6, ["PST"] = -8, ["PDT"] = -7,
        ["BST"] = 1, ["CET"] = 1, ["CEST"] = 2, ["EET"] = 2, ["EEST"] = 3, ["JST"] = 9
    };

    /// <summary>The instant in UTC, or null when the text is no date this parser knows.</summary>
    public static DateTime? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var text = value.Trim();
        if (Rfc822().Match(text) is { Success: true } match) return FromRfc822(match);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
            out var parsed) ? parsed.UtcDateTime : null;
    }

    private static DateTime? FromRfc822(Match match)
    {
        var month = Array.IndexOf(Months, match.Groups["month"].Value[..3].ToLowerInvariant()) + 1;
        var year = int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture);
        if (year < 100) year += 2000; // RFC 822 two-digit years; no feed in use predates 2000.
        var day = int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture);
        var hour = int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture);
        var minute = int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture);
        var second = match.Groups["second"].Success ? int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture) : 0;
        if (month == 0 || day is < 1 or > 31 || hour > 23 || minute > 59 || second > 60 || day > DateTime.DaysInMonth(year, month)) return null;
        var local = new DateTime(year, month, day, hour, minute, Math.Min(second, 59), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Offset(match.Groups["zone"].Value)).UtcDateTime;
    }

    /// <summary>
    /// A numeric offset, a known abbreviation, or UTC: an unknown abbreviation is ambiguous, and UTC keeps the date within
    /// a day of the truth instead of dropping it.
    /// </summary>
    private static TimeSpan Offset(string zone)
    {
        if (zone.Length == 0) return TimeSpan.Zero;
        if (zone[0] is '+' or '-')
        {
            var digits = zone[1..].Replace(":", "", StringComparison.Ordinal);
            var minutes = int.Parse(digits[..2], CultureInfo.InvariantCulture) * 60 + int.Parse(digits[2..], CultureInfo.InvariantCulture);
            // DateTimeOffset accepts at most ±14 hours; a larger value is a typo, read as UTC like an unknown zone.
            return minutes > 14 * 60 ? TimeSpan.Zero : TimeSpan.FromMinutes(zone[0] == '-' ? -minutes : minutes);
        }
        return TimeSpan.FromHours(ZoneHours.GetValueOrDefault(zone));
    }

    [GeneratedRegex(@"^(?:[A-Za-z]{2,9},?\s*)?(?<day>\d{1,2})\s+(?<month>[A-Za-z]{3,9})\.?\s+(?<year>\d{4}|\d{2})\s+(?<hour>\d{1,2}):(?<minute>\d{2})(?::(?<second>\d{2}))?(?:\.\d+)?\s*(?<zone>[+-]\d{2}:?\d{2}|[A-Za-z]{1,5})?$")]
    private static partial Regex Rfc822();
}
