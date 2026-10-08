using Microsoft.EntityFrameworkCore;
using TelegramGateway.Core.Entities;

namespace TelegramGateway.Infrastructure.Persistence;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<DeliveryEntity> Deliveries => Set<DeliveryEntity>();
    public DbSet<MessagePartEntity> MessageParts => Set<MessagePartEntity>();
    public DbSet<DeliveryAttemptEntity> DeliveryAttempts => Set<DeliveryAttemptEntity>();

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
    }
}
