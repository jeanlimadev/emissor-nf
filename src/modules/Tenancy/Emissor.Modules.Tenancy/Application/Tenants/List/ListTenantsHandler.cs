using Emissor.BuildingBlocks;
using Emissor.Modules.Tenancy.Application.Ports;

namespace Emissor.Modules.Tenancy.Application.Tenants.List;

public sealed class ListTenantsHandler(ITenantRepository tenants)
{
    public async Task<PagedResponse<TenantResponse>> HandleAsync(PageRequest? page, CancellationToken cancellationToken)
    {
        var result = await tenants.ListAsync(page, cancellationToken);

        return PagedResponse<TenantResponse>.From(
            page,
            result.Items.Select(TenantResponse.From).ToList(),
            result.Total);
    }
}
