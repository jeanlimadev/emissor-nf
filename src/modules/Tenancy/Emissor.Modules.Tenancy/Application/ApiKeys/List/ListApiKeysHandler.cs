using Emissor.BuildingBlocks;
using Emissor.Modules.Tenancy.Application.Ports;
using Emissor.Modules.Tenancy.Domain;

namespace Emissor.Modules.Tenancy.Application.ApiKeys.List;

public sealed class ListApiKeysHandler(ITenantRepository tenants, IApiKeyRepository apiKeys)
{
    public async Task<Result<IReadOnlyList<ApiKeyResponse>>> HandleAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetAsync(tenantId, cancellationToken);

        if (tenant is null)
            return Result<IReadOnlyList<ApiKeyResponse>>.Failure(TenancyErrors.TenantNaoEncontrado);

        var lista = await apiKeys.ListByTenantAsync(tenantId, cancellationToken);

        return Result<IReadOnlyList<ApiKeyResponse>>.Success(lista.Select(ApiKeyResponse.From).ToList());
    }
}
