using Emissor.Modules.Tenancy.Domain;

namespace Emissor.Modules.Tenancy.Application.Tenants;

public sealed record TenantResponse(Guid Id, string Nome, bool Ativo, DateTimeOffset CriadoEm)
{
    public static TenantResponse From(Tenant tenant) => new(tenant.Id, tenant.Nome, tenant.Ativo, tenant.CriadoEm);
}
