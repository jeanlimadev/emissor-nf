using Emissor.BuildingBlocks;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Application.Webhooks;
using Emissor.Modules.Nfe.Infrastructure.Fila;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Emissor.Modules.Nfe.Infrastructure.Webhooks;

/// <summary>
/// Entrega os webhooks pendentes, no mesmo molde do worker de notas: fila no
/// Postgres com SKIP LOCKED, escopo por entrega com o tenant amarrado, e a
/// entrega em maos termina mesmo durante o desligamento.
/// </summary>
public sealed class WebhookWorker(
    IServiceScopeFactory scopes,
    IOptions<WorkerOptions> options,
    IClock clock,
    ILogger<WebhookWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Ativo)
            return;

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.Value.IntervaloSegundos), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var entregou = false;

            try
            {
                entregou = await EntregarProximaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro no laço do worker de webhooks.");
            }

            if (!entregou)
                await Task.Delay(TimeSpan.FromSeconds(options.Value.IntervaloSegundos), stoppingToken);
        }
    }

    private async Task<bool> EntregarProximaAsync(CancellationToken cancellationToken)
    {
        TrabalhoWebhook? trabalho;

        using (var escopoFila = scopes.CreateScope())
        {
            trabalho = await escopoFila.ServiceProvider.GetRequiredService<IWebhookFila>().RetirarAsync(clock.UtcNow, cancellationToken);
        }

        if (trabalho is null)
            return false;

        using var escopo = scopes.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AmbientTenant>().Bind(trabalho.TenantId, string.Empty);

        using var _ = logger.BeginScope(new Dictionary<string, object> { ["EntregaId"] = trabalho.EntregaId, ["TenantId"] = trabalho.TenantId });
        await escopo.ServiceProvider.GetRequiredService<EntregarWebhookHandler>().EntregarAsync(trabalho.EntregaId, CancellationToken.None);

        return true;
    }
}
