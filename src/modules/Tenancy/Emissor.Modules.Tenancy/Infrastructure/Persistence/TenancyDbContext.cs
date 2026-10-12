using Emissor.Modules.Tenancy.Domain;
using Microsoft.EntityFrameworkCore;

namespace Emissor.Modules.Tenancy.Infrastructure.Persistence;

/// <summary>
/// Banco do modulo, no schema <c>tenancy</c>. Nao tem filtro por tenant: este
/// modulo e justamente quem diz qual tenant e qual.
/// </summary>
public sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options) : DbContext(options)
{
    public const string Schema = "tenancy";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Tenant>(tenant =>
        {
            tenant.ToTable("tenants");
            tenant.HasKey(t => t.Id);
            tenant.Property(t => t.Id).HasColumnName("id");
            tenant.Property(t => t.Nome).HasColumnName("nome").HasMaxLength(Tenant.NomeMaxLength).IsRequired();
            tenant.Property(t => t.Ativo).HasColumnName("ativo");
            tenant.Property(t => t.CriadoEm).HasColumnName("criado_em");
            tenant.HasIndex(t => t.Nome);
        });

        modelBuilder.Entity<ApiKey>(key =>
        {
            key.ToTable("api_keys");
            key.HasKey(k => k.Id);
            key.Property(k => k.Id).HasColumnName("id");
            key.Property(k => k.TenantId).HasColumnName("tenant_id");
            key.Property(k => k.Nome).HasColumnName("nome").HasMaxLength(ApiKey.NomeMaxLength).IsRequired();
            key.Property(k => k.Prefixo).HasColumnName("prefixo").HasMaxLength(ApiKey.PrefixoLength).IsRequired();
            key.Property(k => k.Hash).HasColumnName("hash").HasMaxLength(ApiKey.HashLength).IsRequired();
            key.Property(k => k.CriadaEm).HasColumnName("criada_em");
            key.Property(k => k.RevogadaEm).HasColumnName("revogada_em");
            key.Property(k => k.UltimoUsoEm).HasColumnName("ultimo_uso_em");
            key.HasIndex(k => k.Prefixo);
            key.HasIndex(k => k.TenantId);
            key.HasOne<Tenant>().WithMany().HasForeignKey(k => k.TenantId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
