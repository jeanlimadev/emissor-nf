using System.Globalization;
using System.Xml.Linq;
using Emissor.Contracts.Emitentes;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;

namespace Emissor.Modules.Nfe.Infrastructure.Sefaz;

/// <summary>
/// Monta as mensagens que vao para a SEFAZ e le as que voltam. Sao funcoes
/// puras sobre XML, sem rede, para os testes cobrirem cada formato de retorno
/// que o Manual de Orientacao descreve. Namespace de tudo:
/// <c>http://www.portalfiscal.inf.br/nfe</c>.
/// </summary>
public static class MensagensSefaz
{
    public const string Versao = "4.00";
    public static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";
    public static readonly XNamespace Soap = "http://www.w3.org/2003/05/soap-envelope";

    // ----- Montagem -------------------------------------------------------------------------------

    /// <summary>consStatServ: so pede o status do servico da UF no ambiente.</summary>
    public static string ConsStatServ(int codigoUf, AmbienteSefaz ambiente) =>
        new XElement(Ns + "consStatServ",
            new XAttribute("versao", Versao),
            new XElement(Ns + "tpAmb", (int)ambiente),
            new XElement(Ns + "cUF", codigoUf),
            new XElement(Ns + "xServ", "STATUS")).ToString(SaveOptions.DisableFormatting);

    /// <summary>
    /// enviNFe com uma nota e <c>indSinc=1</c>: a SEFAZ responde com o
    /// protocolo na mesma chamada. O XML da NF-e entra como veio, byte a
    /// byte, porque qualquer mudanca invalidaria a assinatura.
    /// </summary>
    public static string EnviNFe(long idLote, string xmlNfeAssinado) =>
        $"""<enviNFe xmlns="{Ns}" versao="{Versao}"><idLote>{idLote.ToString("000000000000000", CultureInfo.InvariantCulture)}</idLote><indSinc>1</indSinc>{xmlNfeAssinado}</enviNFe>""";

    /// <summary>consSitNFe: situacao atual de uma nota pela chave.</summary>
    public static string ConsSitNFe(AmbienteSefaz ambiente, ChaveAcesso chave) =>
        new XElement(Ns + "consSitNFe",
            new XAttribute("versao", Versao),
            new XElement(Ns + "tpAmb", (int)ambiente),
            new XElement(Ns + "xServ", "CONSULTAR"),
            new XElement(Ns + "chNFe", chave.Valor)).ToString(SaveOptions.DisableFormatting);

    /// <summary>consReciNFe: situacao de um lote recebido (cStat 103), pelo numero do recibo.</summary>
    public static string ConsReciNFe(AmbienteSefaz ambiente, string recibo) =>
        new XElement(Ns + "consReciNFe",
            new XAttribute("versao", Versao),
            new XElement(Ns + "tpAmb", (int)ambiente),
            new XElement(Ns + "nRec", recibo)).ToString(SaveOptions.DisableFormatting);

    /// <summary>Envelope SOAP 1.2 com a mensagem dentro de <c>nfeDadosMsg</c> no namespace do servico.</summary>
    public static string Envelope(ServicoSefaz servico, string mensagem) =>
        $"""<?xml version="1.0" encoding="utf-8"?><soap12:Envelope xmlns:soap12="{Soap}"><soap12:Body><nfeDadosMsg xmlns="{EnderecosSefaz.NamespaceServico(servico)}">{mensagem}</nfeDadosMsg></soap12:Body></soap12:Envelope>""";

    /// <summary>procNFe: a nota assinada mais o protocolo, no formato que se guarda e distribui.</summary>
    public static string ProcNFe(string xmlNfeAssinado, string xmlProtNfe) =>
        $"""<nfeProc xmlns="{Ns}" versao="{Versao}">{xmlNfeAssinado}{xmlProtNfe}</nfeProc>""";

    // ----- Leitura --------------------------------------------------------------------------------

    /// <summary>Abre o envelope de resposta e devolve o primeiro elemento dentro de <c>nfeResultMsg</c>.</summary>
    public static XElement ExtrairResultado(string envelopeResposta)
    {
        XDocument documento;

        try
        {
            documento = XDocument.Parse(envelopeResposta);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new SefazIndisponivelException("A resposta da SEFAZ não é um XML válido.", ex);
        }

        var falha = documento.Descendants(Soap + "Fault").FirstOrDefault();

        if (falha is not null)
            throw new SefazIndisponivelException("A SEFAZ devolveu uma falha SOAP: " + (falha.Descendants(Soap + "Text").FirstOrDefault()?.Value ?? falha.Value));

        var resultado = documento.Descendants().FirstOrDefault(e => e.Name.LocalName == "nfeResultMsg")?.Elements().FirstOrDefault();

        return resultado ?? throw new SefazIndisponivelException("A resposta da SEFAZ veio sem nfeResultMsg.");
    }

    public static StatusServicoSefaz LerRetConsStatServ(XElement retorno)
    {
        Esperar(retorno, "retConsStatServ");

        return new StatusServicoSefaz(
            Inteiro(retorno, "cStat"),
            Texto(retorno, "xMotivo"),
            DataHora(retorno, "dhRecbto"),
            retorno.Element(Ns + "tMed") is { } tMed && int.TryParse(tMed.Value, out var segundos) ? segundos : null);
    }

    public static RetornoAutorizacao LerRetEnviNFe(XElement retorno)
    {
        Esperar(retorno, "retEnviNFe");

        var protNFe = retorno.Element(Ns + "protNFe");
        var recibo = retorno.Element(Ns + "infRec")?.Element(Ns + "nRec")?.Value;

        return new RetornoAutorizacao(
            Inteiro(retorno, "cStat"),
            Texto(retorno, "xMotivo"),
            recibo,
            protNFe is null ? null : LerProtNFe(protNFe));
    }

    public static RetornoConsulta LerRetConsSitNFe(XElement retorno)
    {
        Esperar(retorno, "retConsSitNFe");

        var protNFe = retorno.Element(Ns + "protNFe");

        return new RetornoConsulta(
            Inteiro(retorno, "cStat"),
            Texto(retorno, "xMotivo"),
            protNFe is null ? null : LerProtNFe(protNFe));
    }

    public static RetornoRecibo LerRetConsReciNFe(XElement retorno)
    {
        Esperar(retorno, "retConsReciNFe");

        // Lote de uma nota: o primeiro protNFe e o dela.
        var protNFe = retorno.Element(Ns + "protNFe");

        return new RetornoRecibo(Inteiro(retorno, "cStat"), Texto(retorno, "xMotivo"), protNFe is null ? null : LerProtNFe(protNFe));
    }

    public static ProtocoloSefaz LerProtNFe(XElement protNFe)
    {
        var infProt = protNFe.Element(Ns + "infProt") ?? throw new SefazIndisponivelException("protNFe sem infProt.");

        return new ProtocoloSefaz(
            Inteiro(infProt, "cStat"),
            Texto(infProt, "xMotivo"),
            infProt.Element(Ns + "nProt")?.Value,
            DataHora(infProt, "dhRecbto"),
            protNFe.ToString(SaveOptions.DisableFormatting));
    }

    private static void Esperar(XElement elemento, string nome)
    {
        if (elemento.Name != Ns + nome)
            throw new SefazIndisponivelException($"Esperava {nome} na resposta da SEFAZ e veio {elemento.Name.LocalName}.");
    }

    private static int Inteiro(XElement pai, string nome) =>
        int.TryParse(pai.Element(Ns + nome)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var valor)
            ? valor
            : throw new SefazIndisponivelException($"Resposta da SEFAZ sem o campo {nome}.");

    private static string Texto(XElement pai, string nome) => pai.Element(Ns + nome)?.Value?.Trim() ?? string.Empty;

    private static DateTimeOffset? DataHora(XElement pai, string nome) =>
        DateTimeOffset.TryParse(pai.Element(Ns + nome)?.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var valor) ? valor : null;
}
