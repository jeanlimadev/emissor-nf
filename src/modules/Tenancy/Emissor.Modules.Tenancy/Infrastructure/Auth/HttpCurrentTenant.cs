using System.Security.Claims;
using Emissor.BuildingBlocks;

namespace Emissor.Modules.Tenancy.Infrastructure.Auth;

/// <summary>O tenant de uma requisicao HTTP, lido das claims do principal autenticado por API key.</summary>
public sealed class HttpCurrentTenant(ClaimsPrincipal principal) : ICurrentTenant
{
    private readonly Guid? _tenantId = Guid.TryParse(principal.FindFirstValue(TenantClaims.TenantId), out var id) ? id : null;

    public Guid TenantId => _tenantId ?? throw new InvalidOperationException(
        "Requisição sem tenant autenticado: a rota precisa exigir a política de tenant.");

    public string Nome => principal.FindFirstValue(TenantClaims.TenantNome) ?? string.Empty;

    public bool IsBound => _tenantId is not null;

    public static bool HasTenant(ClaimsPrincipal? principal) =>
        principal?.FindFirstValue(TenantClaims.TenantId) is not null;
}
