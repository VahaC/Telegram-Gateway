using Microsoft.EntityFrameworkCore;

namespace TelegramGateway.Infrastructure.Persistence;

public sealed class OAuthDbContext(DbContextOptions<OAuthDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.UseOpenIddict();
    }
}
