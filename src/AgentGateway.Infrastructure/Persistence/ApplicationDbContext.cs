using Microsoft.EntityFrameworkCore;
using AgentGateway.Core.Entities;

namespace AgentGateway.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<DeliveryEntity> Deliveries => Set<DeliveryEntity>();
    public DbSet<MessagePartEntity> MessageParts => Set<MessagePartEntity>();
    public DbSet<DeliveryAttemptEntity> DeliveryAttempts => Set<DeliveryAttemptEntity>();
    public DbSet<NewsIssueEntity> NewsIssues => Set<NewsIssueEntity>();
    public DbSet<NewsItemEntity> NewsItems => Set<NewsItemEntity>();
    public DbSet<NewsSourceEntity> NewsSources => Set<NewsSourceEntity>();
    public DbSet<NewsReleaseEntity> NewsReleases => Set<NewsReleaseEntity>();
    public DbSet<NewsSecurityAlertEntity> NewsSecurityAlerts => Set<NewsSecurityAlertEntity>();
    public DbSet<NewsRemovalEntity> NewsRemovals => Set<NewsRemovalEntity>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<DeliveryEntity>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.Property(row => row.IdempotencyKey).HasMaxLength(128);
            entity.HasIndex(row => row.IdempotencyKey).IsUnique();
            // ADR 0001: one daily digest per date prevents alternate keys duplicating the daily send.
            entity.HasIndex(row => row.DigestDate).IsUnique();
            entity.Property(row => row.PayloadHash).HasMaxLength(64);
            entity.Property(row => row.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(row => row.ErrorCode).HasMaxLength(64);
            entity.HasMany(row => row.Parts).WithOne().HasForeignKey(row => row.DeliveryId)
                .OnDelete(DeleteBehavior.Cascade); // Parts have no meaning outside the delivery ledger.
        });
        builder.Entity<MessagePartEntity>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.DeliveryId, row.Position }).IsUnique();
            entity.HasMany(row => row.Attempts).WithOne().HasForeignKey(row => row.MessagePartId)
                .OnDelete(DeleteBehavior.Cascade); // Attempts belong to the same retained ledger.
        });
        builder.Entity<DeliveryAttemptEntity>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(row => row.ErrorCode).HasMaxLength(64);
        });
        builder.Entity<NewsIssueEntity>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.Date).IsUnique(); // One issue per Europe/Kyiv calendar day.
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Language).HasMaxLength(16);
            entity.Property(row => row.Intro).HasMaxLength(1000);
            entity.Property(row => row.ResearchMode).HasMaxLength(100);
            entity.Property(row => row.PayloadHash).HasMaxLength(64);
            entity.Property(row => row.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(row => row.ErrorCode).HasMaxLength(64);
            entity.Property(row => row.DayUrl).HasMaxLength(2000);
            // Items, releases, alerts and pending removals have no meaning outside their issue.
            entity.HasMany(row => row.Items).WithOne().HasForeignKey(row => row.IssueId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(row => row.Releases).WithOne().HasForeignKey(row => row.IssueId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(row => row.SecurityAlerts).WithOne().HasForeignKey(row => row.IssueId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(row => row.Removals).WithOne().HasForeignKey(row => row.IssueId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<NewsItemEntity>(entity =>
        {
            entity.HasKey(row => row.Id);
            // The registry of everything ever accepted: the database itself refuses a second item with
            // the same material or event, even if two submissions race past the duplicate check.
            entity.HasIndex(row => row.CanonicalUrl).IsUnique();
            entity.HasIndex(row => row.EventKey).IsUnique();
            entity.HasIndex(row => new { row.IssueId, row.Number }).IsUnique();
            entity.Property(row => row.Category).HasMaxLength(16);
            entity.Property(row => row.Title).HasMaxLength(200);
            entity.Property(row => row.Summary).HasMaxLength(2000);
            entity.Property(row => row.WhyItMatters).HasMaxLength(600);
            entity.Property(row => row.ReleaseStatus).HasMaxLength(16);
            entity.Property(row => row.Version).HasMaxLength(50);
            entity.Property(row => row.EventKey).HasMaxLength(100);
            entity.Property(row => row.SourceUrl).HasMaxLength(2000);
            entity.Property(row => row.SourceName).HasMaxLength(100);
            entity.Property(row => row.CanonicalUrl).HasMaxLength(2000);
            entity.Property(row => row.ImageUrl).HasMaxLength(2000);
            entity.Property(row => row.ImageAlt).HasMaxLength(200);
            entity.Property(row => row.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(row => row.WpUrl).HasMaxLength(2000);
            entity.Property(row => row.ImageResult).HasMaxLength(16);
            entity.Property(row => row.ImageError).HasMaxLength(64);
            entity.Property(row => row.ErrorCode).HasMaxLength(64);
            entity.HasMany(row => row.ExtraSources).WithOne().HasForeignKey(row => row.ItemId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<NewsSourceEntity>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.CanonicalUrl);
            entity.Property(row => row.Url).HasMaxLength(2000);
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.Property(row => row.CanonicalUrl).HasMaxLength(2000);
        });
        builder.Entity<NewsReleaseEntity>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.CanonicalUrl).IsUnique(); // Releases are deduplicated against earlier releases only.
            entity.Property(row => row.Name).HasMaxLength(100);
            entity.Property(row => row.Version).HasMaxLength(50);
            entity.Property(row => row.Url).HasMaxLength(2000);
            entity.Property(row => row.CanonicalUrl).HasMaxLength(2000);
            entity.Property(row => row.Note).HasMaxLength(300);
        });
        builder.Entity<NewsSecurityAlertEntity>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => row.CanonicalUrl).IsUnique(); // Alerts are deduplicated against earlier alerts only.
            entity.Property(row => row.AlertId).HasMaxLength(50);
            entity.Property(row => row.Product).HasMaxLength(100);
            entity.Property(row => row.Severity).HasMaxLength(16);
            entity.Property(row => row.Url).HasMaxLength(2000);
            entity.Property(row => row.CanonicalUrl).HasMaxLength(2000);
            entity.Property(row => row.Note).HasMaxLength(300);
        });
        builder.Entity<NewsRemovalEntity>(entity =>
        {
            entity.HasKey(row => row.Id);
            entity.HasIndex(row => new { row.IssueId, row.EventKey }).IsUnique();
            entity.Property(row => row.EventKey).HasMaxLength(100);
            entity.Property(row => row.Status).HasConversion<string>().HasMaxLength(24);
            entity.Property(row => row.ErrorCode).HasMaxLength(64);
        });
    }
}
