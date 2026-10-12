using Emissor.BuildingBlocks;
using Emissor.Modules.Tenancy.Application.ApiKeys.Create;
using Emissor.Modules.Tenancy.Application.ApiKeys.List;
using Emissor.Modules.Tenancy.Application.ApiKeys.Revoke;
using Emissor.Modules.Tenancy.Application.Tenants.Create;
using Emissor.Modules.Tenancy.Application.Tenants.List;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Emissor.Modules.Tenancy.Presentation.Admin;

public sealed record CreateTenantRequest(string? Nome);
public sealed record CreateApiKeyRequest(string? Nome);

internal static class TenantAdminEndpoints
{
    internal static RouteGroupBuilder MapTenantAdmin(this RouteGroupBuilder group)
    {
        group.MapPost("/tenants", async (
            CreateTenantRequest request,
            CreateTenantHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new CreateTenantCommand(request.Nome), cancellationToken);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível criar o tenant.")
                : Results.Created($"/api/v1/admin/tenants/{result.Value!.Id}", result.Value);
        })
        .WithSummary("Cria um tenant (sistema cliente).");

        group.MapGet("/tenants", async (
            int? page,
            int? pageSize,
            ListTenantsHandler handler,
            CancellationToken cancellationToken) =>
        {
            if (!PageRequest.TryCreate(page, pageSize, out var request, out var pageSizeInvalid))
                return ProblemResults.FromError(
                    pageSizeInvalid ? PageRequest.InvalidPageSize : PageRequest.InvalidPage,
                    "Parâmetros de paginação inválidos.");

            return Results.Ok(await handler.HandleAsync(request, cancellationToken));
        })
        .WithSummary("Lista os tenants.");

        group.MapPost("/tenants/{tenantId:guid}/api-keys", async (
            Guid tenantId,
            CreateApiKeyRequest request,
            CreateApiKeyHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new CreateApiKeyCommand(tenantId, request.Nome), cancellationToken);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível criar a chave de API.")
                : Results.Created($"/api/v1/admin/tenants/{tenantId}/api-keys/{result.Value!.Id}", result.Value);
        })
        .WithSummary("Cria uma chave de API. O texto da chave só aparece nesta resposta.");

        group.MapGet("/tenants/{tenantId:guid}/api-keys", async (
            Guid tenantId,
            ListApiKeysHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(tenantId, cancellationToken);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Tenant não encontrado.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Lista as chaves de API do tenant (só metadados; o texto da chave não é recuperável).");

        group.MapDelete("/tenants/{tenantId:guid}/api-keys/{apiKeyId:guid}", async (
            Guid tenantId,
            Guid apiKeyId,
            RevokeApiKeyHandler handler,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(new RevokeApiKeyCommand(tenantId, apiKeyId), cancellationToken);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível revogar a chave de API.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Revoga uma chave de API.");

        return group;
    }
}
