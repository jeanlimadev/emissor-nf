using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Nfe.Application.Webhooks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Emissor.Modules.Nfe.Presentation;

internal static class WebhookEndpoints
{
    internal static RouteGroupBuilder MapWebhook(this RouteGroupBuilder group)
    {
        group.MapPut("/", async (WebhookRequest request, ConfigurarWebhookHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(request, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível configurar o webhook.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Configura (ou troca) o webhook do tenant. O segredo volta em texto só nesta resposta.")
        .WithDescription("Sem `segredo`, o emissor gera um. A URL precisa ser https (http só em localhost).");

        group.MapGet("/", async (GetWebhookHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Webhook não configurado.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Webhook configurado (sem o segredo).");

        group.MapDelete("/", async (RemoverWebhookHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Webhook não configurado.")
                : Results.NoContent();
        })
        .WithSummary("Remove o webhook. Entregas pendentes passam a falhar.");

        group.MapPost("/testar", async (TestarWebhookHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Webhook não configurado.")
                : Results.Accepted("/api/v1/webhook/entregas", result.Value);
        })
        .WithSummary("Enfileira um evento `webhook.teste` para conferir URL e assinatura.");

        group.MapGet("/entregas", async (Guid? nfeId, int? page, int? pageSize, ListarEntregasHandler handler, CancellationToken ct) =>
        {
            if (!PageRequest.TryCreate(page, pageSize, out var request, out var pageSizeInvalid))
                return ProblemResults.FromError(
                    pageSizeInvalid ? PageRequest.InvalidPageSize : PageRequest.InvalidPage,
                    "Parâmetros de paginação inválidos.");

            return Results.Ok(await handler.HandleAsync(nfeId, request, ct));
        })
        .WithSummary("Entregas do webhook, da mais nova para a mais antiga: status, tentativas, último HTTP e erro.");

        return group;
    }
}
