using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Emissor.Modules.Tenancy.Infrastructure.Persistence;

/// <summary>So para o <c>dotnet ef migrations add</c>: a connection string nao e usada, so o provider.</summary>
public sealed class DesignTimeTenancyDbContextFactory : IDesignTimeDbContextFactory<TenancyDbContext>
{
    public TenancyDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TenancyDbContext>()
            .UseNpgsql("Host=localhost;Database=design", npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", TenancyDbContext.Schema))
            .Options;

        return new TenancyDbContext(options);
    }
}
