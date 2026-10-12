using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Nfe.Application.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Emissor.Modules.Nfe.Infrastructure.Fila;

/// <summary>
/// Uma vez por dia, avisa cada tenant (webhook <c>emitente.certificado_vencendo</c>)
/// dos emitentes cujo certificado vence em ate 30 dias ou ja venceu. O
/// health check <c>certificados</c> cobre o monitoramento; este worker cobre
/// o sistema cliente, que e quem consegue pedir um certificado novo.
/// </summary>
public sealed class CertificadoVencimentoWorker(
    IServiceScopeFactory scopes,
    IOptions<WorkerOptions> options,
    IClock clock,
    ILogger<CertificadoVencimentoWorker> logger) : BackgroundService
{
    public const string Evento = "emitente.certificado_vencendo";
    public static readonly TimeSpan Antecedencia = TimeSpan.FromDays(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Ativo)
            return;

        // Espera a subida (migrations) e depois roda a cada intervalo configurado (padrao: 24 h).
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, options.Value.IntervaloSegundos)), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await AvisarAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Erro ao verificar certificados a vencer.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(options.Value.VerificacaoCertificadosHoras), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task AvisarAsync(CancellationToken cancellationToken)
    {
        var agora = clock.UtcNow;
        IReadOnlyList<CertificadoVencendo> vencendo;

        using (var escopoLeitura = scopes.CreateScope())
        {
            vencendo = await escopoLeitura.ServiceProvider.GetRequiredService<IEmitenteRepository>()
                .ListarCertificadosVencendoAsync(agora + Antecedencia, cancellationToken);
        }

        foreach (var grupo in vencendo.GroupBy(c => c.TenantId))
        {
            using var escopo = scopes.CreateScope();
            escopo.ServiceProvider.GetRequiredService<AmbientTenant>().Bind(grupo.Key, string.Empty);
            var agendador = escopo.ServiceProvider.GetRequiredService<AgendadorDeWebhook>();
            var webhooks = escopo.ServiceProvider.GetRequiredService<Application.Ports.IWebhookRepository>();

            foreach (var certificado in grupo)
            {
                var dias = (int)Math.Floor((certificado.ValidoAte - agora).TotalDays);
                logger.LogWarning("Certificado do emitente {Nome} ({Documento}) vence em {Dias} dias ({ValidoAte:d}).", certificado.Nome, certificado.Documento, dias, certificado.ValidoAte);

                await agendador.AgendarAsync(Evento, null, null,
                    new CertificadoVencendoDto(certificado.EmitenteId, certificado.Nome, certificado.Documento, certificado.ValidoAte, dias), cancellationToken);
            }

            await webhooks.SaveChangesAsync(cancellationToken);
        }
    }
}
