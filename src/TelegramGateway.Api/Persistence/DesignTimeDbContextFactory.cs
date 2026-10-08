using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using TelegramGateway.Infrastructure.Persistence;

namespace TelegramGateway.Api.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
        => new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=Data/gateway.db", options => options.MigrationsAssembly("TelegramGateway.Api")).Options);
}
