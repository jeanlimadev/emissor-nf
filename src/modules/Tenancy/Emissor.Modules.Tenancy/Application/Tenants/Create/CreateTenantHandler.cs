using Emissor.BuildingBlocks;
using Emissor.Modules.Tenancy.Application.Ports;
using Emissor.Modules.Tenancy.Domain;

namespace Emissor.Modules.Tenancy.Application.Tenants.Create;

public sealed record CreateTenantCommand(string? Nome);

public sealed class CreateTenantHandler(ITenantRepository tenants, IClock clock)
{
    public async Task<Result<TenantResponse>> HandleAsync(CreateTenantCommand command, CancellationToken cancellationToken)
    {
        var tenant = Tenant.Criar(command.Nome, clock.UtcNow);

        if (tenant.IsFailure)
            return tenant.FailAs<TenantResponse>();

        await tenants.AddAsync(tenant.Value!, cancellationToken);
        await tenants.SaveChangesAsync(cancellationToken);

        return Result<TenantResponse>.Success(TenantResponse.From(tenant.Value!));
    }
}
