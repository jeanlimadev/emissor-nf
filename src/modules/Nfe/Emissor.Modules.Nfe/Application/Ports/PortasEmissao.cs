using System.Security.Cryptography.X509Certificates;
using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Nfe.Domain;

namespace Emissor.Modules.Nfe.Application.Ports;

/// <summary>Identifica para qual SEFAZ e em qual ambiente a chamada vai. Em contingencia, vai para a SVC-AN.</summary>
public sealed record DestinoSefaz(string Uf, AmbienteSefaz Ambiente, bool Contingencia = false)
{
    public static DestinoSefaz Do(Emissor.Modules.Emitentes.Domain.Emitente emitente) =>
        new(emitente.Endereco.Uf, emitente.Ambiente, emitente.EmContingencia);
}

/// <summary>Retorno do NFeRetAutorizacao4 (consulta do recibo): 104 processado (com protocolo), 105 ainda em processamento.</summary>
public sealed record RetornoRecibo(int CStat, string XMotivo, ProtocoloSefaz? Protocolo);

/// <summary>Retorno do NfeStatusServico4.</summary>
public sealed record StatusServicoSefaz(int CStat, string XMotivo, DateTimeOffset? Recebimento, int? TempoMedioSegundos);

/// <summary>
/// Retorno do NFeAutorizacao4 em modo sincrono. Quando a SEFAZ processa na
/// hora, <see cref="Protocolo"/> vem preenchido; quando ela so recebe o lote
/// (cStat 103), vem o <see cref="Recibo"/> e a nota fica pendente de consulta.
/// </summary>
public sealed record RetornoAutorizacao(int CStat, string XMotivo, string? Recibo, ProtocoloSefaz? Protocolo);

/// <summary>Retorno do NfeConsultaProtocolo4 (consulta pela chave).</summary>
public sealed record RetornoConsulta(int CStat, string XMotivo, ProtocoloSefaz? Protocolo);

/// <summary>
/// A conversa com a SEFAZ. A implementacao conhece URLs, SOAP e TLS; quem usa
/// so ve os retornos ja interpretados. Falha de rede ou resposta ilegivel
/// viram <see cref="SefazIndisponivelException"/>, que o worker trata como
/// "sem resposta conclusiva".
/// </summary>
public interface ISefazGateway : ISefazGatewayEventos
{
    Task<StatusServicoSefaz> ConsultarStatusServicoAsync(DestinoSefaz destino, X509Certificate2 certificado, CancellationToken cancellationToken);

    /// <param name="xmlNfeAssinado">O elemento <c>NFe</c> assinado, sem declaracao XML.</param>
    Task<RetornoAutorizacao> AutorizarAsync(DestinoSefaz destino, X509Certificate2 certificado, string xmlNfeAssinado, long idLote, CancellationToken cancellationToken);

    Task<RetornoConsulta> ConsultarPorChaveAsync(DestinoSefaz destino, X509Certificate2 certificado, ChaveAcesso chave, CancellationToken cancellationToken);

    Task<RetornoRecibo> ConsultarReciboAsync(DestinoSefaz destino, X509Certificate2 certificado, string recibo, CancellationToken cancellationToken);
}

/// <summary>Nao houve resposta util da SEFAZ (timeout, rede, HTTP 5xx, XML inesperado).</summary>
public sealed class SefazIndisponivelException(string mensagem, Exception? causa = null) : Exception(mensagem, causa);

/// <summary>Notas do tenant corrente (o filtro global do DbContext garante).</summary>
public interface INfeRepository
{
    Task<NotaFiscal?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Leitura fresca do banco, sem rastreio: para quem fica esperando a nota mudar (o worker grava por outro contexto).</summary>
    Task<NotaFiscal?> GetSemRastreioAsync(Guid id, CancellationToken cancellationToken);

    Task<NotaFiscal?> GetByIdempotenciaAsync(Guid emitenteId, string chaveIdempotencia, CancellationToken cancellationToken);
    Task<PagedResult<NotaFiscal>> ListAsync(Guid? emitenteId, NfeStatus? status, PageRequest? page, CancellationToken cancellationToken);
    Task AddAsync(NotaFiscal nfe, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Uma nota pronta para o worker, com o tenant a quem ela pertence.</summary>
public sealed record TrabalhoNfe(Guid NfeId, Guid TenantId);

/// <summary>
/// A fila de transmissao, que sao as proprias notas com status de fila e
/// <c>proxima_tentativa_em</c> vencido. Atravessa todos os tenants, por isso
/// e uma porta separada do repositorio: so o worker a usa.
/// </summary>
public interface INfeFila
{
    /// <summary>
    /// Pega uma nota da fila e a marca em processamento, com lock que ignora
    /// linhas ja pegas por outro worker. Tambem retoma notas em processamento
    /// paradas ha mais que <paramref name="posse"/> (processo que caiu no meio).
    /// </summary>
    Task<TrabalhoNfe?> RetirarAsync(DateTimeOffset agora, TimeSpan posse, CancellationToken cancellationToken);
}

/// <summary>Desenha a DANFE (PDF) a partir do XML da nota: <c>nfeProc</c> autorizado ou <c>NFe</c> assinado (previa).</summary>
public interface IDanfeRenderer
{
    /// <param name="marca">Texto da marca d'agua para documentos sem valor (ex.: "PRÉVIA"). Nulo usa a regra padrao (homologacao/sem protocolo).</param>
    byte[] Renderizar(string xml, string? marca);
}

/// <summary>Webhook e entregas do tenant corrente.</summary>
public interface IWebhookRepository
{
    Task<Webhook?> GetAsync(CancellationToken cancellationToken);
    Task<Webhook?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task AddAsync(Webhook webhook, CancellationToken cancellationToken);
    void Remove(Webhook webhook);
    Task AddEntregaAsync(WebhookEntrega entrega, CancellationToken cancellationToken);
    Task<WebhookEntrega?> GetEntregaAsync(Guid id, CancellationToken cancellationToken);
    Task<PagedResult<WebhookEntrega>> ListEntregasAsync(Guid? nfeId, PageRequest? page, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record TrabalhoWebhook(Guid EntregaId, Guid TenantId);

/// <summary>Fila de entregas pendentes, atravessando tenants; so o worker usa.</summary>
public interface IWebhookFila
{
    Task<TrabalhoWebhook?> RetirarAsync(DateTimeOffset agora, CancellationToken cancellationToken);
}

/// <summary>Resultado de um POST ao webhook: status HTTP (nulo quando nem conectou) e erro legivel.</summary>
public sealed record ResultadoEntrega(bool Sucesso, int? HttpStatus, string? Erro);

/// <summary>Faz o POST assinado. Nunca lanca: falha vira <see cref="ResultadoEntrega"/>.</summary>
public interface IWebhookEntregador
{
    Task<ResultadoEntrega> EntregarAsync(Uri url, byte[] segredo, WebhookEntrega entrega, CancellationToken cancellationToken);
}

/// <summary>Retorno do RecepcaoEvento4 para um evento: o <c>retEvento</c> da SEFAZ.</summary>
/// <param name="XmlRetEvento">O elemento <c>retEvento</c> como veio, para compor o <c>procEventoNFe</c>.</param>
public sealed record RetornoEvento(int CStatLote, string XMotivoLote, int? CStat, string? XMotivo, string? Protocolo, DateTimeOffset? Registro, string? XmlRetEvento);

/// <summary>Retorno do NfeInutilizacao4.</summary>
public sealed record RetornoInutilizacao(int CStat, string XMotivo, string? Protocolo, DateTimeOffset? Recebimento, string? XmlRetInut);

public interface ISefazGatewayEventos
{
    /// <param name="xmlEventoAssinado">O elemento <c>evento</c> assinado, sem declaracao XML.</param>
    Task<RetornoEvento> EnviarEventoAsync(DestinoSefaz destino, X509Certificate2 certificado, string xmlEventoAssinado, long idLote, CancellationToken cancellationToken);

    /// <param name="xmlInutAssinado">O elemento <c>inutNFe</c> assinado, sem declaracao XML.</param>
    Task<RetornoInutilizacao> InutilizarAsync(DestinoSefaz destino, X509Certificate2 certificado, string xmlInutAssinado, CancellationToken cancellationToken);
}

/// <summary>Eventos e inutilizacoes do tenant corrente.</summary>
public interface IEventoRepository
{
    Task<EventoNfe?> GetEventoAsync(Guid nfeId, Guid eventoId, CancellationToken cancellationToken);
    Task<IReadOnlyList<EventoNfe>> ListEventosAsync(Guid nfeId, CancellationToken cancellationToken);
    Task AddEventoAsync(EventoNfe evento, CancellationToken cancellationToken);
    Task<Inutilizacao?> GetInutilizacaoAsync(Guid emitenteId, Guid id, CancellationToken cancellationToken);
    Task<IReadOnlyList<Inutilizacao>> ListInutilizacoesAsync(Guid emitenteId, CancellationToken cancellationToken);
    Task AddInutilizacaoAsync(Inutilizacao inutilizacao, CancellationToken cancellationToken);
    /// <summary>Ha nota do emitente com numero na faixa (qualquer status com numero atribuido)?</summary>
    Task<bool> ExisteNotaNaFaixaAsync(Guid emitenteId, int serie, long inicial, long final, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
