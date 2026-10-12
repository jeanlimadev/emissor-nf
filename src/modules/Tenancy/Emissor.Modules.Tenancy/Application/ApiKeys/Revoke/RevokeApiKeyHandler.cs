using Emissor.BuildingBlocks;
using Emissor.Modules.Tenancy.Application.Ports;
using Emissor.Modules.Tenancy.Domain;

namespace Emissor.Modules.Tenancy.Application.ApiKeys.Revoke;

public sealed record RevokeApiKeyCommand(Guid TenantId, Guid ApiKeyId);

public sealed class RevokeApiKeyHandler(IApiKeyRepository apiKeys, IClock clock)
{
    public async Task<Result<ApiKeyResponse>> HandleAsync(RevokeApiKeyCommand command, CancellationToken cancellationToken)
    {
        var apiKey = await apiKeys.GetAsync(command.TenantId, command.ApiKeyId, cancellationToken);

        if (apiKey is null)
            return Result<ApiKeyResponse>.Failure(TenancyErrors.ApiKeyNaoEncontrada);

        apiKey.Revogar(clock.UtcNow);
        await apiKeys.SaveChangesAsync(cancellationToken);

        return Result<ApiKeyResponse>.Success(ApiKeyResponse.From(apiKey));
    }
}
