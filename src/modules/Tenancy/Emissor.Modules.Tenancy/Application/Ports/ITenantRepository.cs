using Emissor.BuildingBlocks;
using Emissor.Modules.Tenancy.Domain;

namespace Emissor.Modules.Tenancy.Application.Ports;

public interface ITenantRepository
{
    Task<Tenant?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<Tenant>> ListAsync(PageRequest? page, CancellationToken cancellationToken);
    Task AddAsync(Tenant tenant, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IApiKeyRepository
{
    Task<ApiKey?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ApiKey>> ListByTenantAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Chaves (ativas ou nao) com este prefixo. Mais de uma e improvavel, mas possivel: quem confere o hash decide.</summary>
    Task<IReadOnlyList<ApiKey>> FindByPrefixoAsync(string prefixo, CancellationToken cancellationToken);

    Task AddAsync(ApiKey apiKey, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
