using Emissor.BuildingBlocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Emissor.Modules.Emitentes.Infrastructure.Persistence;

public sealed class DesignTimeEmitentesDbContextFactory : IDesignTimeDbContextFactory<EmitentesDbContext>
{
    public EmitentesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EmitentesDbContext>()
            .UseNpgsql("Host=localhost;Database=design", npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", EmitentesDbContext.Schema))
            .Options;

        return new EmitentesDbContext(options, new AmbientTenant());
    }
}
