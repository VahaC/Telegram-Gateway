using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using AgentGateway.Infrastructure.Persistence;

namespace AgentGateway.Api.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=Data/gateway.db", options => options.MigrationsAssembly("AgentGateway.Api")).Options);
}
