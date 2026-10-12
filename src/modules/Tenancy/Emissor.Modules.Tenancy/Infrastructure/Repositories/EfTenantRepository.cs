using Emissor.BuildingBlocks;
using Emissor.Modules.Tenancy.Application.Ports;
using Emissor.Modules.Tenancy.Domain;
using Emissor.Modules.Tenancy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Emissor.Modules.Tenancy.Infrastructure.Repositories;

internal sealed class EfTenantRepository(TenancyDbContext db) : ITenantRepository
{
    public Task<Tenant?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Tenants.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<PagedResult<Tenant>> ListAsync(PageRequest? page, CancellationToken cancellationToken)
    {
        var query = db.Tenants.OrderBy(t => t.Nome).ThenBy(t => t.Id);

        if (page is null)
            return PagedResult<Tenant>.Whole(await query.ToListAsync(cancellationToken));

        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip(page.Skip).Take(page.Take).ToListAsync(cancellationToken);

        return new PagedResult<Tenant>(items, total);
    }

    public async Task AddAsync(Tenant tenant, CancellationToken cancellationToken) =>
        await db.Tenants.AddAsync(tenant, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

internal sealed class EfApiKeyRepository(TenancyDbContext db) : IApiKeyRepository
{
    public Task<ApiKey?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.ApiKeys.FirstOrDefaultAsync(k => k.TenantId == tenantId && k.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ApiKey>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.ApiKeys.Where(k => k.TenantId == tenantId).OrderBy(k => k.CriadaEm).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ApiKey>> FindByPrefixoAsync(string prefixo, CancellationToken cancellationToken) =>
        await db.ApiKeys.Where(k => k.Prefixo == prefixo).ToListAsync(cancellationToken);

    public async Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken) =>
        await db.ApiKeys.AddAsync(apiKey, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
