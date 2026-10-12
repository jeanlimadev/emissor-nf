using Emissor.Modules.Emitentes.Application.Ports;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Emissor.Api.Infrastructure;

/// <summary>
/// Degraded quando algum certificado vence em menos de 30 dias; Unhealthy
/// quando algum ja venceu (o emitente nao consegue emitir). O detalhe lista
/// os emitentes, para o alerta de monitoramento dizer quem e.
/// </summary>
public sealed class CertificadosHealthCheck(IServiceScopeFactory scopes, Emissor.BuildingBlocks.IClock clock) : IHealthCheck
{
    public static readonly TimeSpan Aviso = TimeSpan.FromDays(30);

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var agora = clock.UtcNow;
        var vencendo = await scope.ServiceProvider.GetRequiredService<IEmitenteRepository>().ListarCertificadosVencendoAsync(agora + Aviso, cancellationToken);

        if (vencendo.Count == 0)
            return HealthCheckResult.Healthy("Nenhum certificado vence nos próximos 30 dias.");

        var dados = vencendo.ToDictionary(c => $"{c.Nome} ({c.Documento})", c => (object)c.ValidoAte.ToString("yyyy-MM-dd"));
        var vencidos = vencendo.Count(c => c.ValidoAte <= agora);

        return vencidos > 0
            ? HealthCheckResult.Unhealthy($"{vencidos} certificado(s) vencido(s).", data: dados)
            : HealthCheckResult.Degraded($"{vencendo.Count} certificado(s) vencem em até 30 dias.", data: dados);
    }
}
