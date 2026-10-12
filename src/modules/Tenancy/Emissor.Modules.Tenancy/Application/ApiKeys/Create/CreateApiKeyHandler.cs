using Emissor.BuildingBlocks;
using Emissor.Modules.Tenancy.Application.Ports;
using Emissor.Modules.Tenancy.Domain;

namespace Emissor.Modules.Tenancy.Application.ApiKeys.Create;

public sealed record CreateApiKeyCommand(Guid TenantId, string? Nome);

public sealed class CreateApiKeyHandler(ITenantRepository tenants, IApiKeyRepository apiKeys, IClock clock)
{
    public async Task<Result<ApiKeyCreatedResponse>> HandleAsync(CreateApiKeyCommand command, CancellationToken cancellationToken)
    {
        var tenant = await tenants.GetAsync(command.TenantId, cancellationToken);

        if (tenant is null)
            return Result<ApiKeyCreatedResponse>.Failure(TenancyErrors.TenantNaoEncontrado);

        var created = ApiKeyFactory.Criar(tenant.Id, command.Nome, clock.UtcNow);

        if (created.IsFailure)
            return created.FailAs<ApiKeyCreatedResponse>();

        var (apiKey, chave) = created.Value;

        await apiKeys.AddAsync(apiKey, cancellationToken);
        await apiKeys.SaveChangesAsync(cancellationToken);

        return Result<ApiKeyCreatedResponse>.Success(
            new ApiKeyCreatedResponse(apiKey.Id, apiKey.Nome, apiKey.Prefixo, chave, apiKey.CriadaEm));
    }
}
