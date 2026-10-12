using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;

namespace Emissor.Modules.Nfe.Domain;

/// <summary>
/// Um evento sobre uma nota autorizada: cancelamento (110111) ou carta de
/// correcao (110110). Guarda o XML assinado que foi transmitido e, quando a
/// SEFAZ registra, o <c>procEventoNFe</c> (evento + retorno).
/// </summary>
public sealed class EventoNfe : ITenantOwned
{
    public const int JustificativaMin = 15;
    public const int JustificativaMax = 255;
    public const int CorrecaoMin = 15;
    public const int CorrecaoMax = 1000;
    public const int MaxCartasCorrecao = 20;

    /// <summary>Prazo para cancelar, contado da autorizacao. 24 h e a regra geral (Ajuste SINIEF 07/05), e a do RJ.</summary>
    public static readonly TimeSpan PrazoCancelamento = TimeSpan.FromHours(24);

    private EventoNfe() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid NfeId { get; private set; }
    public TipoEventoNfe Tipo { get; private set; }
    public int Sequencia { get; private set; }
    public string Texto { get; private set; } = string.Empty;
    public EventoStatus Status { get; private set; }
    public DateTimeOffset DataEvento { get; private set; }
    public string? XmlEvento { get; private set; }
    public string? XmlProcEvento { get; private set; }
    public string? Protocolo { get; private set; }
    public int? CStat { get; private set; }
    public string? XMotivo { get; private set; }
    public DateTimeOffset? RegistradoEm { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }

    public static Result<EventoNfe> Cancelamento(NotaFiscal nota, string? justificativa, DateTimeOffset agora)
    {
        if (nota.Status != NfeStatus.Autorizada || nota.DataAutorizacao is null)
            return Result<EventoNfe>.Failure(NfeErrors.CancelamentoSoDeAutorizada);

        if (agora - nota.DataAutorizacao.Value > PrazoCancelamento)
            return Result<EventoNfe>.Failure(NfeErrors.CancelamentoForaDoPrazo);

        var texto = Limpar(justificativa);

        if (texto.Length is < JustificativaMin or > JustificativaMax)
            return Result<EventoNfe>.Failure(NfeErrors.JustificativaInvalida);

        return Result<EventoNfe>.Success(Novo(nota, TipoEventoNfe.Cancelamento, 1, texto, agora));
    }

    public static Result<EventoNfe> CartaCorrecao(NotaFiscal nota, string? correcao, int cartasAnteriores, DateTimeOffset agora)
    {
        if (nota.Status != NfeStatus.Autorizada)
            return Result<EventoNfe>.Failure(NfeErrors.CartaSoDeAutorizada);

        if (cartasAnteriores >= MaxCartasCorrecao)
            return Result<EventoNfe>.Failure(NfeErrors.CartaLimiteAtingido);

        var texto = Limpar(correcao);

        if (texto.Length is < CorrecaoMin or > CorrecaoMax)
            return Result<EventoNfe>.Failure(NfeErrors.CorrecaoInvalida);

        return Result<EventoNfe>.Success(Novo(nota, TipoEventoNfe.CartaCorrecao, cartasAnteriores + 1, texto, agora));
    }

    private static EventoNfe Novo(NotaFiscal nota, TipoEventoNfe tipo, int sequencia, string texto, DateTimeOffset agora) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = nota.TenantId,
            NfeId = nota.Id,
            Tipo = tipo,
            Sequencia = sequencia,
            Texto = texto,
            Status = EventoStatus.Pendente,
            DataEvento = agora,
            CriadoEm = agora
        };

    /// <summary>Campo <c>Id</c> do <c>infEvento</c>: "ID" + tpEvento + chave + nSeqEvento (2 digitos).</summary>
    public string IdInfEvento(string chaveAcesso) => $"ID{(int)Tipo}{chaveAcesso}{Sequencia:00}";

    public void DefinirXmlAssinado(string xml) => XmlEvento = xml;

    public void RegistrarRetorno(int cStat, string xMotivo, string? protocolo, DateTimeOffset? registradoEm, string? xmlProcEvento)
    {
        CStat = cStat;
        XMotivo = xMotivo.Length <= NotaFiscal.XMotivoMaxLength ? xMotivo : xMotivo[..NotaFiscal.XMotivoMaxLength];
        Protocolo = protocolo;
        RegistradoEm = registradoEm?.ToUniversalTime();
        XmlProcEvento = xmlProcEvento;
        Status = CodigosSefaz.EhEventoRegistrado(cStat) ? EventoStatus.Registrado : EventoStatus.Rejeitado;
    }

    private static string Limpar(string? texto) =>
        string.Join(' ', (texto ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

/// <summary>
/// Inutilizacao de uma faixa de numeros nunca usados (furo na numeracao).
/// Por layout, so emitente CNPJ: o <c>infInut</c> do layout 4.00 nao tem CPF.
/// </summary>
public sealed class Inutilizacao : ITenantOwned
{
    private Inutilizacao() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid EmitenteId { get; private set; }
    public int Serie { get; private set; }
    public long NumeroInicial { get; private set; }
    public long NumeroFinal { get; private set; }
    public int Ano { get; private set; }
    public string Justificativa { get; private set; } = string.Empty;
    public EventoStatus Status { get; private set; }
    public string? XmlInutilizacao { get; private set; }
    public string? XmlProcInutilizacao { get; private set; }
    public string? Protocolo { get; private set; }
    public int? CStat { get; private set; }
    public string? XMotivo { get; private set; }
    public DateTimeOffset? HomologadaEm { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }

    public static Result<Inutilizacao> Criar(Guid tenantId, Guid emitenteId, TipoEmitente tipoEmitente, int serie, long inicial, long final, string? justificativa, DateTimeOffset agora)
    {
        if (tipoEmitente != TipoEmitente.PessoaJuridica)
            return Result<Inutilizacao>.Failure(NfeErrors.InutilizacaoSoParaCnpj);

        if (serie is < 0 or > 999)
            return Result<Inutilizacao>.Failure(NfeErrors.ChaveSerieInvalida);

        if (inicial < 1 || final < inicial || final > 999_999_999)
            return Result<Inutilizacao>.Failure(NfeErrors.FaixaInvalida);

        var texto = string.Join(' ', (justificativa ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        if (texto.Length is < EventoNfe.JustificativaMin or > EventoNfe.JustificativaMax)
            return Result<Inutilizacao>.Failure(NfeErrors.JustificativaInvalida);

        return Result<Inutilizacao>.Success(new Inutilizacao
        {
            Id = Guid.CreateVersion7(),
            TenantId = tenantId,
            EmitenteId = emitenteId,
            Serie = serie,
            NumeroInicial = inicial,
            NumeroFinal = final,
            Ano = agora.Year % 100,
            Justificativa = texto,
            Status = EventoStatus.Pendente,
            CriadaEm = agora
        });
    }

    /// <summary>Campo <c>Id</c> do <c>infInut</c>: "ID" + cUF + ano(2) + CNPJ(14) + mod(2) + serie(3) + nNFIni(9) + nNFFin(9).</summary>
    public string IdInfInut(int codigoUf, string cnpj) =>
        $"ID{codigoUf:00}{Ano:00}{cnpj}{ChaveAcesso.ModeloNfe:00}{Serie:000}{NumeroInicial:000000000}{NumeroFinal:000000000}";

    public void DefinirXmlAssinado(string xml) => XmlInutilizacao = xml;

    public void RegistrarRetorno(int cStat, string xMotivo, string? protocolo, DateTimeOffset? homologadaEm, string? xmlProc)
    {
        CStat = cStat;
        XMotivo = xMotivo.Length <= NotaFiscal.XMotivoMaxLength ? xMotivo : xMotivo[..NotaFiscal.XMotivoMaxLength];
        Protocolo = protocolo;
        HomologadaEm = homologadaEm?.ToUniversalTime();
        XmlProcInutilizacao = xmlProc;
        Status = cStat == CodigosSefaz.InutilizacaoHomologada ? EventoStatus.Registrado : EventoStatus.Rejeitado;
    }
}
