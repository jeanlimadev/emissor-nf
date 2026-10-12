using Emissor.BuildingBlocks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Emissor.Modules.Tenancy.Presentation.Me;

public sealed record MeResponse(Guid TenantId, string Nome);

/// <summary>Para o sistema cliente conferir que a chave funciona e qual tenant ela abre.</summary>
internal static class MeEndpoint
{
    internal static RouteGroupBuilder MapMe(this RouteGroupBuilder group)
    {
        group.MapGet("/me", (ICurrentTenant tenant) => Results.Ok(new MeResponse(tenant.TenantId, tenant.Nome)))
            .WithSummary("Identifica o tenant da chave de API apresentada.");

        return group;
    }
}
