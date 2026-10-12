using Emissor.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Emissor.Modules.Nfe.Infrastructure.Persistence;

public sealed class DesignTimeNfeDbContextFactory : IDesignTimeDbContextFactory<NfeDbContext>
{
    public NfeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NfeDbContext>()
            .UseNpgsql("Host=localhost;Database=design", npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", NfeDbContext.Schema))
            .Options;

        return new NfeDbContext(options, new AmbientTenant());
    }
}
