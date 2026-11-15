using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using AgentGateway.Core.Entities;
using AgentGateway.Core.Options;
using AgentGateway.Infrastructure.Persistence;

namespace AgentGateway.Api.Services.News;

/// <summary>
/// Brings the feeds table in line with the catalog shipped in the image: new feeds are added, changed ones updated,
/// feeds that left the catalog are disabled (their entries stay until they age out). Runs once when the collector starts.
/// </summary>
public sealed class NewsFeedCatalogSync(ApplicationDbContext database, IOptions<NewsOptions> options, TimeProvider time,
    ILogger<NewsFeedCatalogSync> logger)
{
    /// <summary>Returns false when the catalog file could not be read; the table is then left exactly as it was.</summary>
    public async Task<bool> SyncAsync(CancellationToken cancellationToken)
    {
        var path = ResolvePath(options.Value.FeedCatalogPath);
        var catalog = NewsFeedCatalogLoader.Load(path);
        if (!catalog.Readable)
        {
            // Disabling every feed because of a broken file would empty the leads; the last good state keeps working instead.
            logger.LogError("News feed catalog not applied, the feeds table is unchanged: {Problem}", catalog.Errors[0]);
            return false;
        }
        foreach (var error in catalog.Errors) logger.LogWarning("News feed catalog entry skipped: {Problem}", error);
        var now = time.GetUtcNow().UtcDateTime;
        var stored = await database.NewsFeeds.ToDictionaryAsync(feed => feed.CatalogId, StringComparer.Ordinal, cancellationToken);
        int added = 0, updated = 0, disabled = 0;
        foreach (var feed in catalog.Feeds)
        {
            if (!stored.Remove(feed.Id!, out var row))
            {
                database.NewsFeeds.Add(row = new NewsFeedEntity { CatalogId = feed.Id!, CreatedUtc = now });
                added++;
            }
            else if (row.Name != feed.Name || row.Url != feed.Url || !row.Categories.SequenceEqual(feed.Categories!) || row.Kind != feed.Kind
                     || row.Language != feed.Language || !row.Enabled)
                updated++;
            if (row.Url != feed.Url)
            {
                // Validators belong to the old address; sending them to the new one could earn a wrong 304.
                row.ETag = null;
                row.LastModified = null;
            }
            row.Name = feed.Name!;
            row.Url = feed.Url!;
            row.Categories = [.. feed.Categories!];
            row.Kind = feed.Kind!;
            row.Language = feed.Language!;
            row.Enabled = true;
        }
        foreach (var row in stored.Values.Where(row => row.Enabled))
        {
            row.Enabled = false;
            disabled++;
        }
        await database.SaveChangesAsync(cancellationToken);
        logger.LogInformation("News feed catalog applied from {Path}: {Feeds} feeds, {Added} added, {Updated} updated, {Disabled} disabled, {Skipped} skipped.",
            path, catalog.Feeds.Count, added, updated, disabled, catalog.Errors.Count);
        return true;
    }

    /// <summary>A relative path points into the application folder, where the build and the Docker image ship the catalog.</summary>
    public static string ResolvePath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
