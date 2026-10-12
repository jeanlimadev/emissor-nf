using System.Globalization;
using System.Xml.Linq;

namespace Emissor.Modules.Nfe.Infrastructure.Danfe;

/// <summary>
/// Le o XML da nota (elemento <c>NFe</c> ou <c>nfeProc</c>) e extrai o que a
/// DANFE precisa. Tolerante a grupos ausentes: campo opcional no layout vira
/// nulo ou zero aqui. So depende de XML, para a DANFE nao depender da
/// biblioteca de NF-e nem do pedido original.
/// </summary>
public static class DanfeLeitor
{
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";

    public static DanfeDados Ler(string xml)
    {
        var raiz = XDocument.Parse(xml).Root ?? throw new ArgumentException("XML vazio.", nameof(xml));
        var nfe = raiz.Name == Ns + "NFe" ? raiz : raiz.Element(Ns + "NFe") ?? throw new ArgumentException("XML sem o elemento NFe.", nameof(xml));
        var infNFe = nfe.Element(Ns + "infNFe") ?? throw new ArgumentException("XML sem infNFe.", nameof(xml));
        var ide = infNFe.Element(Ns + "ide")!;
        var emit = infNFe.Element(Ns + "emit")!;
        var dest = infNFe.Element(Ns + "dest");
        var total = infNFe.Element(Ns + "total")?.Element(Ns + "ICMSTot");
        var ibsCbsTot = infNFe.Element(Ns + "total")?.Element(Ns + "IBSCBSTot");
        var transp = infNFe.Element(Ns + "transp");
        var infAdic = infNFe.Element(Ns + "infAdic");
        var protNFe = raiz.Element(Ns + "protNFe");

        return new DanfeDados(
            ChaveAcesso: infNFe.Attribute("Id")!.Value[3..],
            Homologacao: Texto(ide, "tpAmb") == "2",
            TipoOperacao: Inteiro(ide, "tpNF"),
            Serie: Inteiro(ide, "serie"),
            Numero: long.Parse(Texto(ide, "nNF"), CultureInfo.InvariantCulture),
            NaturezaOperacao: Texto(ide, "natOp"),
            DataEmissao: DataHora(ide, "dhEmi") ?? default,
            DataSaidaEntrada: DataHora(ide, "dhSaiEnt"),
            Emitente: LerParte(emit, Ns + "enderEmit"),
            InscricaoEstadualSt: TextoOuNulo(emit, "IEST"),
            RegimeTributario: Inteiro(emit, "CRT"),
            Destinatario: dest is null ? ParteVazia : LerParte(dest, Ns + "enderDest"),
            Protocolo: protNFe?.Element(Ns + "infProt") is { } infProt
                ? new DanfeProtocolo(Texto(infProt, "nProt"), DataHora(infProt, "dhRecbto"), Inteiro(infProt, "cStat"), Texto(infProt, "xMotivo"))
                : null,
            Totais: new DanfeTotais(
                Decimal(total, "vBC"), Decimal(total, "vICMS"), Decimal(total, "vICMSDeson"), Decimal(total, "vFCP"),
                Decimal(total, "vBCST"), Decimal(total, "vST"), Decimal(total, "vProd"), Decimal(total, "vFrete"),
                Decimal(total, "vSeg"), Decimal(total, "vDesc"), Decimal(total, "vOutro"), Decimal(total, "vIPI"),
                Decimal(total, "vNF"), DecimalOuNulo(total, "vTotTrib"),
                DecimalOuNulo(ibsCbsTot, "vBCIBSCBS"),
                DecimalOuNulo(ibsCbsTot?.Element(Ns + "gIBS"), "vIBS"),
                DecimalOuNulo(ibsCbsTot?.Element(Ns + "gCBS"), "vCBS")),
            Transporte: LerTransporte(transp),
            Itens: infNFe.Elements(Ns + "det").Select(LerItem).ToList(),
            InformacoesComplementares: TextoOuNulo(infAdic, "infCpl"),
            InformacoesFisco: TextoOuNulo(infAdic, "infAdFisco"));
    }

    private static readonly DanfeParte ParteVazia = new(string.Empty, null, string.Empty, null, string.Empty, string.Empty, null, string.Empty, string.Empty, null);

    private static DanfeParte LerParte(XElement parte, XName nomeEndereco)
    {
        var endereco = parte.Element(nomeEndereco);
        var documento = TextoOuNulo(parte, "CNPJ") is { } cnpj ? FormatarCnpj(cnpj)
            : TextoOuNulo(parte, "CPF") is { } cpf ? FormatarCpf(cpf)
            : TextoOuNulo(parte, "idEstrangeiro") ?? string.Empty;

        var logradouro = Texto(endereco, "xLgr");
        var numero = TextoOuNulo(endereco, "nro");
        var complemento = TextoOuNulo(endereco, "xCpl");

        return new DanfeParte(
            Texto(parte, "xNome"),
            TextoOuNulo(parte, "xFant"),
            documento,
            TextoOuNulo(parte, "IE"),
            string.Join(", ", new[] { logradouro, numero, complemento }.Where(s => !string.IsNullOrWhiteSpace(s))),
            Texto(endereco, "xBairro"),
            TextoOuNulo(endereco, "CEP") is { } cep ? FormatarCep(cep) : null,
            Texto(endereco, "xMun"),
            Texto(endereco, "UF"),
            TextoOuNulo(endereco, "fone") is { } fone ? FormatarTelefone(fone) : null);
    }

    private static DanfeTransporte LerTransporte(XElement? transp)
    {
        var transporta = transp?.Element(Ns + "transporta");

        return new DanfeTransporte(
            transp is null ? null : InteiroOuNulo(transp, "modFrete"),
            TextoOuNulo(transporta, "xNome"),
            TextoOuNulo(transporta, "CNPJ") is { } cnpj ? FormatarCnpj(cnpj) : TextoOuNulo(transporta, "CPF") is { } cpf ? FormatarCpf(cpf) : null,
            TextoOuNulo(transporta, "IE"),
            TextoOuNulo(transporta, "xEnder"),
            TextoOuNulo(transporta, "xMun"),
            TextoOuNulo(transporta, "UF"),
            transp?.Elements(Ns + "vol").Select(v => new DanfeVolume(
                InteiroOuNulo(v, "qVol"), TextoOuNulo(v, "esp"), TextoOuNulo(v, "marca"), TextoOuNulo(v, "nVol"),
                DecimalOuNulo(v, "pesoB"), DecimalOuNulo(v, "pesoL"))).ToList() ?? []);
    }

    private static DanfeItem LerItem(XElement det)
    {
        var prod = det.Element(Ns + "prod")!;
        var imposto = det.Element(Ns + "imposto");
        var icms = imposto?.Element(Ns + "ICMS")?.Elements().FirstOrDefault();
        var ipiTrib = imposto?.Element(Ns + "IPI")?.Element(Ns + "IPITrib");

        var origem = Texto(icms, "orig");
        var cst = TextoOuNulo(icms, "CST") ?? TextoOuNulo(icms, "CSOSN") ?? string.Empty;

        return new DanfeItem(
            Texto(prod, "cProd"),
            Texto(prod, "xProd"),
            TextoOuNulo(det, "infAdProd"),
            Texto(prod, "NCM"),
            origem + cst,
            Inteiro(prod, "CFOP"),
            Texto(prod, "uCom"),
            Decimal(prod, "qCom"),
            Decimal(prod, "vUnCom"),
            Decimal(prod, "vProd"),
            Decimal(icms, "vBC"),
            Decimal(icms, "vICMS"),
            Decimal(icms, "pICMS"),
            Decimal(ipiTrib, "vIPI"),
            Decimal(ipiTrib, "pIPI"));
    }

    // ----- utilitarios ----------------------------------------------------------------------------

    private static string Texto(XElement? pai, string nome) => pai?.Element(Ns + nome)?.Value?.Trim() ?? string.Empty;

    private static string? TextoOuNulo(XElement? pai, string nome)
    {
        var valor = pai?.Element(Ns + nome)?.Value?.Trim();
        return string.IsNullOrEmpty(valor) ? null : valor;
    }

    private static int Inteiro(XElement? pai, string nome) => InteiroOuNulo(pai, nome) ?? 0;

    private static int? InteiroOuNulo(XElement? pai, string nome) =>
        int.TryParse(TextoOuNulo(pai, nome), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static decimal Decimal(XElement? pai, string nome) => DecimalOuNulo(pai, nome) ?? 0m;

    private static decimal? DecimalOuNulo(XElement? pai, string nome) =>
        decimal.TryParse(TextoOuNulo(pai, nome), NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static DateTimeOffset? DataHora(XElement? pai, string nome) =>
        DateTimeOffset.TryParse(TextoOuNulo(pai, nome), CultureInfo.InvariantCulture, DateTimeStyles.None, out var v) ? v : null;

    public static string FormatarCnpj(string cnpj) =>
        cnpj.Length == 14 ? $"{cnpj[..2]}.{cnpj[2..5]}.{cnpj[5..8]}/{cnpj[8..12]}-{cnpj[12..]}" : cnpj;

    public static string FormatarCpf(string cpf) =>
        cpf.Length == 11 ? $"{cpf[..3]}.{cpf[3..6]}.{cpf[6..9]}-{cpf[9..]}" : cpf;

    public static string FormatarCep(string cep) => cep.Length == 8 ? $"{cep[..5]}-{cep[5..]}" : cep;

    public static string FormatarTelefone(string fone) => fone.Length switch
    {
        10 => $"({fone[..2]}) {fone[2..6]}-{fone[6..]}",
        11 => $"({fone[..2]}) {fone[2..7]}-{fone[7..]}",
        _ => fone
    };

    /// <summary>Chave em grupos de 4, como a DANFE imprime abaixo do codigo de barras.</summary>
    public static string FormatarChave(string chave) =>
        string.Join(' ', Enumerable.Range(0, chave.Length / 4).Select(i => chave.Substring(i * 4, 4)));
}
