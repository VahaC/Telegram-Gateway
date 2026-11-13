using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using AgentGateway.Infrastructure.Persistence;

namespace AgentGateway.Api.Persistence;

public sealed class OAuthDesignTimeDbContextFactory : IDesignTimeDbContextFactory<OAuthDbContext>
{
    public OAuthDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<OAuthDbContext>()
            .UseSqlite("Data Source=Data/oauth.db", options => options.MigrationsAssembly("AgentGateway.Api"))
            .UseOpenIddict().Options);
}
