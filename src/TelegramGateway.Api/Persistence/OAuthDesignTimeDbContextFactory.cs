using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TelegramGateway.Infrastructure.Persistence;

namespace TelegramGateway.Api.Persistence;

public sealed class OAuthDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OAuthDbContext>
{
    public OAuthDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<OAuthDbContext>()
            .UseSqlite("Data Source=Data/oauth.db", options => options.MigrationsAssembly("TelegramGateway.Api"))
            .UseOpenIddict().Options);
}
