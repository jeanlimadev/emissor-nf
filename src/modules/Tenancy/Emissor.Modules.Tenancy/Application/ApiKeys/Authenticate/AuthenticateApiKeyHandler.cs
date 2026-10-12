using Emissor.BuildingBlocks;
using Emissor.Modules.Tenancy.Application.Ports;
using Emissor.Modules.Tenancy.Domain;

namespace Emissor.Modules.Tenancy.Application.ApiKeys.Authenticate;

/// <summary>O que o middleware de autenticacao recebe quando a chave confere.</summary>
public sealed record AuthenticatedTenant(Guid TenantId, string TenantNome, Guid ApiKeyId);

/// <summary>
/// Resolve uma chave apresentada no <c>X-Api-Key</c>. Acha pelo prefixo e
/// confere o hash em tempo constante; registra o ultimo uso de forma
/// oportunista (nao e trilha de auditoria, e so um sinal de chave esquecida).
/// </summary>
public sealed class AuthenticateApiKeyHandler(IApiKeyRepository apiKeys, ITenantRepository tenants, IClock clock)
{
    public async Task<Result<AuthenticatedTenant>> HandleAsync(string? chave, CancellationToken cancellationToken)
    {
        var prefixo = ApiKeyGenerator.ExtrairPrefixo(chave);

        if (prefixo is null)
            return Result<AuthenticatedTenant>.Failure(TenancyErrors.ApiKeyInvalida);

        var candidatas = await apiKeys.FindByPrefixoAsync(prefixo, cancellationToken);
        var apiKey = candidatas.FirstOrDefault(k => k.Ativa && ApiKeyGenerator.Confere(k, chave!));

        if (apiKey is null)
            return Result<AuthenticatedTenant>.Failure(TenancyErrors.ApiKeyInvalida);

        var tenant = await tenants.GetAsync(apiKey.TenantId, cancellationToken);

        if (tenant is null || !tenant.Ativo)
            return Result<AuthenticatedTenant>.Failure(TenancyErrors.TenantInativo);

        apiKey.RegistrarUso(clock.UtcNow);
        await apiKeys.SaveChangesAsync(cancellationToken);

        return Result<AuthenticatedTenant>.Success(new AuthenticatedTenant(tenant.Id, tenant.Nome, apiKey.Id));
    }
}
