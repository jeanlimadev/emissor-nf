using Emissor.BuildingBlocks;
using Emissor.Modules.Nfe.Application.Emissao;
using Emissor.Modules.Nfe.Application.Ports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Emissor.Modules.Nfe.Infrastructure.Fila;

public sealed class WorkerOptions
{
    public const string Secao = "Nfe:Worker";

    /// <summary>Desligar em instancias que so servem a API (ou em testes que processam na mao).</summary>
    public bool Ativo { get; set; } = true;

    /// <summary>Quanto esperar quando a fila esta vazia.</summary>
    public double IntervaloSegundos { get; set; } = 2;

    /// <summary>Primeira espera apos uma tentativa sem resposta conclusiva; dobra a cada tentativa.</summary>
    public double EsperaBaseSegundos { get; set; } = 30;

    /// <summary>Teto da espera entre tentativas.</summary>
    public double EsperaMaximaSegundos { get; set; } = 1800;

    /// <summary>
    /// Posse de uma nota em processamento. Se o processo cair no meio, outra
    /// instancia a retoma depois deste prazo (e consulta a SEFAZ antes de reenviar).
    /// Maior que o timeout da SEFAZ, para nao retomar uma nota que ainda esta em transmissao.
    /// </summary>
    public double PosseSegundos { get; set; } = 300;

    /// <summary>De quantas em quantas horas o aviso de certificado a vencer roda.</summary>
    public double VerificacaoCertificadosHoras { get; set; } = 24;

    /// <summary>30 s, 1 min, 2 min... ate o teto.</summary>
    public TimeSpan EsperaParaTentativa(int tentativa) =>
        TimeSpan.FromSeconds(Math.Min(EsperaBaseSegundos * Math.Pow(2, Math.Max(0, tentativa - 1)), EsperaMaximaSegundos));
}

/// <summary>
/// Tira notas da fila e as processa, uma por vez, cada uma no seu escopo de
/// servicos com o tenant amarrado. A fila fica no Postgres (a propria tabela
/// de notas), entao varias instancias podem rodar o worker ao mesmo tempo.
/// </summary>
public sealed class NfeWorker(
    IServiceScopeFactory scopes,
    IOptions<WorkerOptions> options,
    IClock clock,
    ILogger<NfeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Ativo)
        {
            logger.LogInformation("Worker de NF-e desligado por configuração.");
            return;
        }

        logger.LogInformation("Worker de NF-e ligado (intervalo {Intervalo} s).", options.Value.IntervaloSegundos);

        // Um intervalo antes da primeira consulta: na subida, as migrations podem ainda estar rodando.
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
            var processou = false;

            try
            {
                processou = await ProcessarProximaAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Falha ao retirar da fila (banco fora, por exemplo). Espera e tenta de novo; nunca derruba o host.
                logger.LogError(ex, "Erro no laço do worker de NF-e.");
            }

            if (!processou)
                await Task.Delay(TimeSpan.FromSeconds(options.Value.IntervaloSegundos), stoppingToken);
        }
    }

    private async Task<bool> ProcessarProximaAsync(CancellationToken cancellationToken)
    {
        TrabalhoNfe? trabalho;

        // Escopo sem tenant: a fila atravessa todos os tenants.
        using (var escopoFila = scopes.CreateScope())
        {
            trabalho = await escopoFila.ServiceProvider.GetRequiredService<INfeFila>()
                .RetirarAsync(clock.UtcNow, TimeSpan.FromSeconds(options.Value.PosseSegundos), cancellationToken);
        }

        if (trabalho is null)
            return false;

        // Escopo da nota: o tenant dela amarrado antes de qualquer repositorio existir.
        using var escopo = scopes.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AmbientTenant>().Bind(trabalho.TenantId, string.Empty);

        using var _ = logger.BeginScope(new Dictionary<string, object> { ["NfeId"] = trabalho.NfeId, ["TenantId"] = trabalho.TenantId });

        // A nota em maos vai ate o fim mesmo durante o desligamento: interromper entre transmitir e gravar
        // deixaria o estado no banco atrasado em relacao a SEFAZ. O host espera o ExecuteAsync terminar.
        await escopo.ServiceProvider.GetRequiredService<ProcessarNfeHandler>().ProcessarAsync(trabalho.NfeId, CancellationToken.None);

        return true;
    }
}
