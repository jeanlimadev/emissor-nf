using System.Globalization;
using System.Xml.Linq;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;

namespace Emissor.Modules.Nfe.Infrastructure.Sefaz;

/// <summary>
/// Mensagens de eventos (leiaute de eventos 1.00) e de inutilizacao (4.00):
/// montagem do XML a assinar, envelope de lote e leitura dos retornos.
/// Funcoes puras sobre XML, como <see cref="MensagensSefaz"/>.
/// </summary>
public static class MensagensEventos
{
    public const string VersaoEvento = "1.00";
    private static readonly XNamespace Ns = MensagensSefaz.Ns;

    /// <summary>Texto fixo exigido no <c>xCondUso</c> da carta de correcao (versao sem acentos, aceita pelo schema).</summary>
    public const string CondicoesDeUsoCartaCorrecao =
        "A Carta de Correcao e disciplinada pelo paragrafo 1o-A do art. 7o do Convenio S/N, de 15 de dezembro de 1970 e pode ser utilizada para regularizacao de erro ocorrido na emissao de documento fiscal, desde que o erro nao esteja relacionado com: I - as variaveis que determinam o valor do imposto tais como: base de calculo, aliquota, diferenca de preco, quantidade, valor da operacao ou da prestacao; II - a correcao de dados cadastrais que implique mudanca do remetente ou do destinatario; III - a data de emissao ou de saida.";

    // ----- Montagem -------------------------------------------------------------------------------

    /// <summary>O elemento <c>evento</c> (ainda sem assinatura) de um cancelamento ou carta de correcao.</summary>
    public static string Evento(EventoNfe evento, NotaFiscal nota, Emitentes.Domain.Emitente emitente, string chaveAcesso, DateTimeOffset dataEvento)
    {
        var detEvento = new XElement(Ns + "detEvento", new XAttribute("versao", VersaoEvento));

        if (evento.Tipo == TipoEventoNfe.Cancelamento)
        {
            detEvento.Add(
                new XElement(Ns + "descEvento", "Cancelamento"),
                new XElement(Ns + "nProt", nota.Protocolo),
                new XElement(Ns + "xJust", evento.Texto));
        }
        else
        {
            detEvento.Add(
                new XElement(Ns + "descEvento", "Carta de Correcao"),
                new XElement(Ns + "xCorrecao", evento.Texto),
                new XElement(Ns + "xCondUso", CondicoesDeUsoCartaCorrecao));
        }

        var infEvento = new XElement(Ns + "infEvento",
            new XAttribute("Id", evento.IdInfEvento(chaveAcesso)),
            new XElement(Ns + "cOrgao", Uf.Codigo(emitente.Endereco.Uf)),
            new XElement(Ns + "tpAmb", (int)emitente.Ambiente),
            emitente.Tipo == TipoEmitente.PessoaFisica ? new XElement(Ns + "CPF", emitente.Documento) : new XElement(Ns + "CNPJ", emitente.Documento),
            new XElement(Ns + "chNFe", chaveAcesso),
            new XElement(Ns + "dhEvento", dataEvento.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)),
            new XElement(Ns + "tpEvento", (int)evento.Tipo),
            new XElement(Ns + "nSeqEvento", evento.Sequencia),
            new XElement(Ns + "verEvento", VersaoEvento),
            detEvento);

        return new XElement(Ns + "evento", new XAttribute("versao", VersaoEvento), infEvento).ToString(SaveOptions.DisableFormatting);
    }

    /// <summary>Lote de um evento so.</summary>
    public static string EnvEvento(long idLote, string xmlEventoAssinado) =>
        $"""<envEvento xmlns="{Ns}" versao="{VersaoEvento}"><idLote>{idLote.ToString("000000000000000", CultureInfo.InvariantCulture)}</idLote>{xmlEventoAssinado}</envEvento>""";

    /// <summary>procEventoNFe: evento assinado + retEvento, o que se guarda e distribui.</summary>
    public static string ProcEventoNFe(string xmlEventoAssinado, string xmlRetEvento) =>
        $"""<procEventoNFe xmlns="{Ns}" versao="{VersaoEvento}">{xmlEventoAssinado}{xmlRetEvento}</procEventoNFe>""";

    /// <summary>O elemento <c>inutNFe</c> (ainda sem assinatura).</summary>
    public static string InutNFe(Inutilizacao inutilizacao, Emitentes.Domain.Emitente emitente)
    {
        var codigoUf = Uf.Codigo(emitente.Endereco.Uf);

        var infInut = new XElement(Ns + "infInut",
            new XAttribute("Id", inutilizacao.IdInfInut(codigoUf, emitente.Documento)),
            new XElement(Ns + "tpAmb", (int)emitente.Ambiente),
            new XElement(Ns + "xServ", "INUTILIZAR"),
            new XElement(Ns + "cUF", codigoUf),
            new XElement(Ns + "ano", inutilizacao.Ano.ToString("00", CultureInfo.InvariantCulture)),
            new XElement(Ns + "CNPJ", emitente.Documento),
            new XElement(Ns + "mod", ChaveAcesso.ModeloNfe),
            new XElement(Ns + "serie", inutilizacao.Serie),
            new XElement(Ns + "nNFIni", inutilizacao.NumeroInicial),
            new XElement(Ns + "nNFFin", inutilizacao.NumeroFinal),
            new XElement(Ns + "xJust", inutilizacao.Justificativa));

        return new XElement(Ns + "inutNFe", new XAttribute("versao", MensagensSefaz.Versao), infInut).ToString(SaveOptions.DisableFormatting);
    }

    public static string ProcInutNFe(string xmlInutAssinado, string xmlRetInut) =>
        $"""<procInutNFe xmlns="{Ns}" versao="{MensagensSefaz.Versao}">{xmlInutAssinado}{xmlRetInut}</procInutNFe>""";

    // ----- Leitura --------------------------------------------------------------------------------

    public static RetornoEvento LerRetEnvEvento(XElement retorno)
    {
        if (retorno.Name != Ns + "retEnvEvento")
            throw new SefazIndisponivelException($"Esperava retEnvEvento e veio {retorno.Name.LocalName}.");

        var cStatLote = Inteiro(retorno, "cStat");
        var xMotivoLote = Texto(retorno, "xMotivo");
        var retEvento = retorno.Element(Ns + "retEvento");
        var infEvento = retEvento?.Element(Ns + "infEvento");

        if (infEvento is null)
            return new RetornoEvento(cStatLote, xMotivoLote, null, null, null, null, null);

        return new RetornoEvento(
            cStatLote,
            xMotivoLote,
            Inteiro(infEvento, "cStat"),
            Texto(infEvento, "xMotivo"),
            infEvento.Element(Ns + "nProt")?.Value,
            DataHora(infEvento, "dhRegEvento"),
            retEvento!.ToString(SaveOptions.DisableFormatting));
    }

    public static RetornoInutilizacao LerRetInutNFe(XElement retorno)
    {
        if (retorno.Name != Ns + "retInutNFe")
            throw new SefazIndisponivelException($"Esperava retInutNFe e veio {retorno.Name.LocalName}.");

        var infInut = retorno.Element(Ns + "infInut") ?? throw new SefazIndisponivelException("retInutNFe sem infInut.");

        return new RetornoInutilizacao(
            Inteiro(infInut, "cStat"),
            Texto(infInut, "xMotivo"),
            infInut.Element(Ns + "nProt")?.Value,
            DataHora(infInut, "dhRecbto"),
            retorno.ToString(SaveOptions.DisableFormatting));
    }

    private static int Inteiro(XElement pai, string nome) =>
        int.TryParse(pai.Element(Ns + nome)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor)
            ? valor
            : throw new SefazIndisponivelException($"Resposta da SEFAZ sem o campo {nome}.");

    private static string Texto(XElement pai, string nome) => pai.Element(Ns + nome)?.Value?.Trim() ?? string.Empty;

    private static DateTimeOffset? DataHora(XElement pai, string nome) =>
        DateTimeOffset.TryParse(pai.Element(Ns + nome)?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valor) ? valor : null;
}
