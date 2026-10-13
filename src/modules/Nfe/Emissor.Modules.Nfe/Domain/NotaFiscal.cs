using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;

namespace Emissor.Modules.Nfe.Domain;

/// <summary>
/// Uma NF-e pedida por um sistema cliente, do recebimento ate a resposta da
/// SEFAZ. Guarda o pedido original (para montar e remontar o XML), o XML
/// assinado que foi transmitido e, quando autorizada, o <c>procNFe</c> com o
/// protocolo. As transicoes de status estao todas aqui: quem muda o estado da
/// nota explica por que, e o resto do sistema so le.
/// </summary>
public sealed class NotaFiscal : ITenantOwned
{
    public const int ChaveIdempotenciaMaxLength = 128;
    public const int XMotivoMaxLength = 255;
    public const int MaxTentativas = 12;

    private NotaFiscal() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EmitenteId { get; private set; }
    public AmbienteSefaz Ambiente { get; private set; }

    /// <summary>Chave que o cliente manda no header <c>Idempotency-Key</c>: o mesmo pedido duas vezes nao vira duas notas.</summary>
    public string ChaveIdempotencia { get; private set; } = string.Empty;

    /// <summary>Hash do pedido, para detectar a mesma chave de idempotencia com conteudo diferente.</summary>
    public string PedidoHash { get; private set; } = string.Empty;

    public string PedidoJson { get; private set; } = string.Empty;
    public int Serie { get; private set; }
    public long? Numero { get; private set; }
    public string? ChaveAcesso { get; private set; }
    public NfeStatus Status { get; private set; }
    public DateTimeOffset? DataEmissao { get; private set; }
    public string? XmlAssinado { get; private set; }
    public string? XmlProcNfe { get; private set; }
    public string? Protocolo { get; private set; }
    public string? Recibo { get; private set; }
    public int? CStat { get; private set; }
    public string? XMotivo { get; private set; }
    public DateTimeOffset? DataAutorizacao { get; private set; }
    public int Tentativas { get; private set; }
    public DateTimeOffset? ProximaTentativaEm { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset AtualizadaEm { get; private set; }

    public static NotaFiscal Receber(
        Guid tenantId,
        Guid emitenteId,
        AmbienteSefaz ambiente,
        int serie,
        string chaveIdempotencia,
        string pedidoHash,
        string pedidoJson,
        DateTimeOffset agora) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EmitenteId = emitenteId,
            Ambiente = ambiente,
            Serie = serie,
            ChaveIdempotencia = chaveIdempotencia,
            PedidoHash = pedidoHash,
            PedidoJson = pedidoJson,
            Status = NfeStatus.Recebida,
            ProximaTentativaEm = agora,
            CriadaEm = agora,
            AtualizadaEm = agora
        };

    public bool EstaNaFila => Status is NfeStatus.Recebida or NfeStatus.PendenteConsulta;
    public bool EhFinal => Status is NfeStatus.Autorizada or NfeStatus.Rejeitada or NfeStatus.Denegada or NfeStatus.Cancelada or NfeStatus.Falha;

    /// <summary>O worker pegou a nota da fila.</summary>
    public Result IniciarProcessamento(DateTimeOffset agora)
    {
        if (!EstaNaFila)
            return Result.Failure(NfeErrors.TransicaoInvalida(Status, NfeStatus.EmProcessamento));

        Status = NfeStatus.EmProcessamento;
        ProximaTentativaEm = null;
        Tentativas++;
        AtualizadaEm = agora;

        return Result.Success();
    }

    /// <summary>Numero reservado na serie e chave calculada. So acontece uma vez: reenvio da mesma nota mantem numero e serie.</summary>
    public void DefinirNumeroEChave(long numero, ChaveAcesso chave, DateTimeOffset dataEmissao, DateTimeOffset agora)
    {
        Numero = numero;
        ChaveAcesso = chave.Valor;
        // Datas ficam em UTC no banco (o Postgres so aceita offset zero); o XML guarda o fuso do emitente.
        DataEmissao = dataEmissao.ToUniversalTime();
        AtualizadaEm = agora;
    }

    public void DefinirXmlAssinado(string xml, DateTimeOffset agora)
    {
        XmlAssinado = xml;
        AtualizadaEm = agora;
    }

    public void RegistrarAutorizacao(ProtocoloSefaz protocolo, string xmlProcNfe, DateTimeOffset agora)
    {
        Status = NfeStatus.Autorizada;
        Protocolo = protocolo.Numero;
        CStat = protocolo.CStat;
        XMotivo = Truncar(protocolo.XMotivo);
        DataAutorizacao = protocolo.Recebimento?.ToUniversalTime();
        XmlProcNfe = xmlProcNfe;
        ProximaTentativaEm = null;
        AtualizadaEm = agora;
    }

    public void RegistrarDenegacao(ProtocoloSefaz protocolo, string? xmlProcNfe, DateTimeOffset agora)
    {
        Status = NfeStatus.Denegada;
        Protocolo = protocolo.Numero;
        CStat = protocolo.CStat;
        XMotivo = Truncar(protocolo.XMotivo);
        XmlProcNfe = xmlProcNfe;
        ProximaTentativaEm = null;
        AtualizadaEm = agora;
    }

    public void RegistrarRejeicao(int cStat, string xMotivo, DateTimeOffset agora)
    {
        Status = NfeStatus.Rejeitada;
        CStat = cStat;
        XMotivo = Truncar(xMotivo);
        ProximaTentativaEm = null;
        AtualizadaEm = agora;
    }

    /// <summary>
    /// Sem resposta conclusiva (rede caiu, lote ainda em processamento, servico
    /// paralisado). A nota volta para a fila com espera crescente; antes de
    /// reenviar, o worker consulta pela chave para nao duplicar.
    /// </summary>
    public void MarcarPendenteConsulta(string motivo, string? recibo, TimeSpan espera, DateTimeOffset agora)
    {
        Status = NfeStatus.PendenteConsulta;
        XMotivo = Truncar(motivo);
        Recibo = recibo ?? Recibo;
        ProximaTentativaEm = agora + espera;
        AtualizadaEm = agora;
    }

    /// <summary>O recibo nao serve mais (expirou ou a SEFAZ nao o reconhece): a proxima verificacao e pela chave.</summary>
    public void LimparRecibo() => Recibo = null;

    /// <summary>A consulta disse que a nota nao consta na SEFAZ: pode transmitir de novo o mesmo XML.</summary>
    public void VoltarParaFila(string motivo, DateTimeOffset agora)
    {
        Status = NfeStatus.Recebida;
        XMotivo = Truncar(motivo);
        ProximaTentativaEm = agora;
        AtualizadaEm = agora;
    }

    /// <summary>Evento de cancelamento registrado pela SEFAZ: a nota existe, mas sem efeito fiscal.</summary>
    public Result RegistrarCancelamento(EventoNfe evento, DateTimeOffset agora)
    {
        if (Status != NfeStatus.Autorizada)
            return Result.Failure(NfeErrors.TransicaoInvalida(Status, NfeStatus.Cancelada));

        if (evento.Tipo != Contracts.Nfe.TipoEventoNfe.Cancelamento || evento.Status != Contracts.Nfe.EventoStatus.Registrado)
            return Result.Failure(NfeErrors.CancelamentoNaoRegistrado);

        Status = NfeStatus.Cancelada;
        XMotivo = Truncar($"{evento.CStat} - {evento.XMotivo}");
        AtualizadaEm = agora;

        return Result.Success();
    }

    public void RegistrarFalha(string motivo, DateTimeOffset agora)
    {
        Status = NfeStatus.Falha;
        XMotivo = Truncar(motivo);
        ProximaTentativaEm = null;
        AtualizadaEm = agora;
    }

    /// <summary>
    /// Rejeitada ou com falha, o cliente corrige o pedido e reenvia. Numero e
    /// serie ficam; a chave e o XML sao refeitos, porque o conteudo mudou.
    /// </summary>
    public Result Reenviar(string? novoPedidoJson, string? novoPedidoHash, DateTimeOffset agora)
    {
        // Pendente de consulta: a nota pode ja estar na SEFAZ, entao nada e refeito;
        // reenviar sem corpo so antecipa a proxima tentativa (consulta pela chave e, se nao constar, transmissao).
        if (Status == NfeStatus.PendenteConsulta)
        {
            if (novoPedidoJson is not null)
                return Result.Failure(NfeErrors.ReenvioPendenteComPedido);

            ProximaTentativaEm = agora;
            AtualizadaEm = agora;
            return Result.Success();
        }

        if (Status is not (NfeStatus.Rejeitada or NfeStatus.Falha))
            return Result.Failure(NfeErrors.ReenvioNaoPermitido);

        // 204 "Duplicidade de NF-e": a SEFAZ ja tem ESTA chave, ou seja, a nota foi autorizada e a resposta se
        // perdeu. Reenviar com chave nova criaria a segunda nota; o certo e consultar. Chave e XML ficam.
        if (CStat == CodigosSefaz.DuplicidadeDeNfe)
        {
            if (novoPedidoJson is not null)
                return Result.Failure(NfeErrors.ReenvioPendenteComPedido);

            Status = NfeStatus.PendenteConsulta;
            Tentativas = Math.Max(Tentativas, 1);
            ProximaTentativaEm = agora;
            AtualizadaEm = agora;
            return Result.Success();
        }

        if (novoPedidoJson is not null && novoPedidoHash is not null)
        {
            PedidoJson = novoPedidoJson;
            PedidoHash = novoPedidoHash;
        }

        // 539 "Duplicidade com diferenca na chave": o numero desta nota ja pertence a OUTRA nota autorizada na
        // SEFAZ (serie que recomecou, numeracao de outro sistema). O numero esta perdido para esta nota: solta-o,
        // e o worker reserva o proximo da serie. Qualquer outra rejeicao mantem o numero, que nunca foi usado.
        if (CStat == CodigosSefaz.DuplicidadeComDiferencaNaChave)
            Numero = null;

        Status = NfeStatus.Recebida;
        ChaveAcesso = null;
        XmlAssinado = null;
        CStat = null;
        XMotivo = null;
        Tentativas = 0;
        ProximaTentativaEm = agora;
        AtualizadaEm = agora;

        return Result.Success();
    }

    public bool EsgotouTentativas => Tentativas >= MaxTentativas;

    private static string Truncar(string texto) =>
        texto.Length <= XMotivoMaxLength ? texto : texto[..XMotivoMaxLength];
}

/// <summary>O que a SEFAZ devolve sobre uma nota (grupo <c>protNFe/infProt</c>).</summary>
/// <param name="XmlProtNfe">O elemento <c>protNFe</c> como veio, para compor o <c>procNFe</c>.</param>
public sealed record ProtocoloSefaz(
    int CStat,
    string XMotivo,
    string? Numero,
    DateTimeOffset? Recebimento,
    string? XmlProtNfe);

/// <summary>Os codigos de retorno da SEFAZ que mudam o rumo da nota. Qualquer outro cStat de nota e rejeicao.</summary>
public static class CodigosSefaz
{
    public const int Autorizada = 100;
    public const int Cancelada = 101;
    public const int LoteRecebido = 103;
    public const int LoteProcessado = 104;
    public const int LoteEmProcessamento = 105;
    public const int ServicoEmOperacao = 107;
    public const int ServicoParalisadoMomentaneamente = 108;
    public const int ServicoParalisadoSemPrevisao = 109;
    public const int NaoConstaNaBase = 217;
    public const int DuplicidadeDeNfe = 204;
    public const int DuplicidadeComDiferencaNaChave = 539;
    public const int EventoRegistradoEVinculado = 135;
    public const int EventoRegistradoNaoVinculado = 136;
    public const int CancelamentoHomologadoForaDoPrazo = 155;
    public const int LoteDeEventoProcessado = 128;
    public const int InutilizacaoHomologada = 102;

    public static bool EhEventoRegistrado(int cStat) => cStat is EventoRegistradoEVinculado or EventoRegistradoNaoVinculado or CancelamentoHomologadoForaDoPrazo;

    public static bool EhDenegacao(int cStat) => cStat is 110 or 301 or 302 or 303;

    /// <summary>Retornos que pedem para tentar depois, sem reenviar agora.</summary>
    public static bool EhTemporario(int cStat) => cStat is LoteRecebido or LoteEmProcessamento or ServicoParalisadoMomentaneamente or ServicoParalisadoSemPrevisao;
}
