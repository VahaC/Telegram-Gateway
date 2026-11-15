using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AgentGateway.Api.Contracts.News;
using AgentGateway.Core.Entities;
using AgentGateway.Core.News;
using AgentGateway.Core.Options;
using AgentGateway.Infrastructure.Persistence;

namespace AgentGateway.Api.Services.News;

/// <summary>Reads the collected feed entries for the MCP tools and the HTTP routes (ADR 0004).</summary>
public sealed class NewsFeedService(ApplicationDbContext database, NewsDuplicateFinder duplicates, IOptions<NewsOptions> options, TimeProvider time)
{
    public const int MaxSinceHours = 168;
    public const int MaxLimit = 50;
    /// <summary>Twenty alternatives cover a reader's stack in one call; more would turn the filter into a full scan of every text.</summary>
    public const int MaxQueryTerms = 20;
    public const int MaxTermLength = 100;
    /// <summary>Enough of the text to judge a lead; the whole text costs one get_news_feed_entry call.</summary>
    public const int ListSummaryLength = 200;
    /// <summary>
    /// Cloud agents truncate or refuse large tool results: a 60 KB page made the 15.11 Haiku run retry with smaller limits.
    /// A page ends before its JSON would pass this size.
    /// </summary>
    public const int MaxPageBytes = 25_000;
    /// <summary>Room for total, nextCursor and the health block, and for the HTTP routes' longer seven-digit date format.</summary>
    private const int EnvelopeBytes = 2_000;
    private const int MaxCursorLength = 100;
    private const string DisabledError = "The feed collector is off on this gateway (News:FeedsEnabled=false); research without the feed tools.";

    // Measures an entry as the MCP layer prints it at worst (non-ASCII escaped), so the real page is never larger than measured.
    private static readonly JsonSerializerOptions MeasureOptions = new(JsonSerializerDefaults.Web);

    public async Task<NewsFeedQueryResult<NewsFeedResponse>> GetFeedAsync(int sinceHours, string? category, string? query,
        bool excludePublished, int limit, string? cursor, CancellationToken cancellationToken)
    {
        if (!options.Value.FeedsEnabled) return new(NewsFeedQueryStatus.Disabled, Error: DisabledError);
        if (sinceHours is < 1 or > MaxSinceHours) return Invalid($"sinceHours must be between 1 and {MaxSinceHours}; got {sinceHours}.");
        if (limit is < 1 or > MaxLimit) return Invalid($"limit must be between 1 and {MaxLimit}; got {limit}.");
        category = string.IsNullOrWhiteSpace(category) ? null : category.Trim();
        if (category is not null && !NewsCategories.All.Contains(category))
            return Invalid($"category '{Shorten(category, 40)}' is unknown; use one of {string.Join(", ", NewsCategories.All)}.");
        var terms = (query ?? "").Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (terms.Count > MaxQueryTerms) return Invalid($"query has {terms.Count} terms; at most {MaxQueryTerms} alternatives separated by | are allowed.");
        if (terms.FirstOrDefault(term => term.Length > MaxTermLength) is { } longTerm)
            return Invalid($"query term '{Shorten(longTerm, 40)}' has {longTerm.Length} characters; at most {MaxTermLength} are allowed.");
        var (offset, asOfUtc) = (0, time.GetUtcNow().UtcDateTime);
        if (!string.IsNullOrEmpty(cursor))
        {
            if (Decode(cursor) is not { } position) return Invalid("cursor is not a nextCursor from get_news_feed; start again without it.");
            (offset, asOfUtc) = position;
        }
        // The window and the snapshot are anchored at the first page, so following pages neither skip nor repeat
        // entries when a poll adds new ones in between.
        var since = asOfUtc.AddHours(-sinceHours);
        var entries = database.NewsFeedEntries.AsNoTracking().Where(entry => entry.SortUtc >= since && entry.FirstSeenUtc <= asOfUtc);
        // A feed dropped from the catalog is disabled with its entries kept; they leave the list at once, otherwise a removed
        // noisy feed (Arch package updates, dev.to) would keep crowding the pages for the whole 14-day retention.
        entries = entries.Where(entry => database.NewsFeeds.Any(feed => feed.Id == entry.FeedId && feed.Enabled));
        if (category is not null)
            entries = entries.Where(entry => database.NewsFeeds.Any(feed => feed.Id == entry.FeedId && feed.Categories.Contains(category)));
        if (terms.Count > 0)
        {
            // Any term matches (OR), so one call can cover a reader's whole stack: Proxmox|ZFS|WireGuard. SQLite's LIKE
            // ignores case for Latin letters only; the feeds are overwhelmingly English.
            var patterns = terms.Select(term => "%" + term.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal) + "%").ToList();
            entries = entries.Where(entry => patterns.Any(pattern =>
                EF.Functions.Like(entry.Title, pattern, "\\") || EF.Functions.Like(entry.Summary, pattern, "\\")));
        }
        if (excludePublished)
        {
            // The same registry check_news_candidates reports from, evaluated inside the one SQL statement.
            var known = duplicates.KnownUrls();
            entries = entries.Where(entry => !known.Contains(entry.CanonicalUrl));
        }
        var total = await entries.CountAsync(cancellationToken);
        var rows = await entries.Join(database.NewsFeeds, entry => entry.FeedId, feed => feed.Id, (entry, feed) => new { Entry = entry, Feed = feed })
            .OrderByDescending(row => row.Entry.SortUtc).ThenByDescending(row => row.Entry.Id)
            .Skip(offset).Take(limit).ToListAsync(cancellationToken);
        var page = new List<NewsFeedEntryResponse>();
        var budget = MaxPageBytes - EnvelopeBytes;
        foreach (var row in rows)
        {
            var entry = ToResponse(row.Entry, row.Feed, Shorten(row.Entry.Summary, ListSummaryLength));
            var size = JsonSerializer.SerializeToUtf8Bytes(entry, MeasureOptions).Length + 1;
            if (page.Count > 0 && size > budget) break;
            budget -= size;
            page.Add(entry);
        }
        var next = offset + page.Count < total ? Encode(offset + page.Count, asOfUtc) : null;
        return new(NewsFeedQueryStatus.Ok, new(page, total, next, await HealthAsync(cancellationToken)));
    }

    public async Task<NewsFeedQueryResult<NewsFeedEntryResponse>> GetEntryAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!options.Value.FeedsEnabled) return new(NewsFeedQueryStatus.Disabled, Error: DisabledError);
        var row = await database.NewsFeedEntries.AsNoTracking().Where(entry => entry.Id == id)
            .Join(database.NewsFeeds, entry => entry.FeedId, feed => feed.Id, (entry, feed) => new { Entry = entry, Feed = feed })
            .SingleOrDefaultAsync(cancellationToken);
        return row is null ? new(NewsFeedQueryStatus.NotFound) : new(NewsFeedQueryStatus.Ok, ToResponse(row.Entry, row.Feed, row.Entry.Summary));
    }

    private async Task<NewsFeedHealthResponse> HealthAsync(CancellationToken cancellationToken)
    {
        var feeds = await database.NewsFeeds.AsNoTracking().Where(feed => feed.Enabled)
            .Select(feed => new { feed.ConsecutiveFailures, feed.LastSuccessUtc }).ToListAsync(cancellationToken);
        var last = feeds.Max(feed => feed.LastSuccessUtc);
        return new(feeds.Count, feeds.Count(feed => feed.ConsecutiveFailures > 0), last is { } value ? Utc(value) : null);
    }

    private static NewsFeedEntryResponse ToResponse(NewsFeedEntryEntity entry, NewsFeedEntity feed, string summary) => new(
        entry.Id, feed.CatalogId, feed.Name, feed.Categories, feed.Kind, entry.Title, entry.Url,
        entry.PublishedUtc is { } published ? Utc(published) : null, Utc(entry.FirstSeenUtc), summary, entry.ImageUrl);

    // SQLite hands DateTime back as Unspecified and the MCP serializer has no UtcDateTimeConverter; the kind keeps the Z.
    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static NewsFeedQueryResult<NewsFeedResponse> Invalid(string error) => new(NewsFeedQueryStatus.Invalid, Error: error);

    private static string Shorten(string text, int maxLength)
    {
        if (text.Length <= maxLength) return text;
        var length = maxLength - 1;
        if (char.IsHighSurrogate(text[length - 1])) length--;
        return text[..length].TrimEnd() + "…";
    }

    /// <summary>The cursor is opaque to callers: the offset of the next page and the snapshot instant of the first one.</summary>
    private static string Encode(int offset, DateTime asOfUtc)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"v1.{offset}.{asOfUtc.Ticks}")));

    private static (int Offset, DateTime AsOfUtc)? Decode(string cursor)
    {
        if (cursor.Length > MaxCursorLength) return null;
        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split('.');
            if (parts is ["v1", var offsetText, var ticksText]
                && int.TryParse(offsetText, NumberStyles.None, CultureInfo.InvariantCulture, out var offset)
                && long.TryParse(ticksText, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks) && ticks <= DateTime.MaxValue.Ticks)
                return (offset, new DateTime(ticks, DateTimeKind.Utc));
        }
        catch (FormatException) { }
        return null;
    }
}
