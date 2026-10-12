using System.Collections.Concurrent;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;

namespace Emissor.Api.Tests;

/// <summary>
/// Uma SEFAZ de mentira para os testes de API. Decide pelo codigo do primeiro
/// produto da nota:
/// <list type="bullet">
/// <item><c>REJEITAR</c>: protocolo com cStat 778 (NCM inexistente).</item>
/// <item><c>SEM-RESPOSTA</c>: a primeira transmissao cai na rede, mas a nota entrou; a consulta devolve 100.</item>
/// <item><c>LOTE-103</c>: a SEFAZ so recebe o lote (103 + recibo); a consulta devolve 100.</item>
/// <item>qualquer outro: autorizada na hora (104 + protNFe 100).</item>
/// </list>
/// Estado estatico porque cada classe de teste sobe seu proprio host (e worker) sobre o mesmo banco.
/// </summary>
public sealed class FakeSefazGateway : ISefazGateway
{
    private static readonly XNamespace Ns = "http://www.portalfiscal.inf.br/nfe";

    /// <summary>Chaves que "entraram" na SEFAZ falsa, com o protocolo gerado.</summary>
    private static readonly ConcurrentDictionary<string, ProtocoloSefaz> Autorizadas = new();

    /// <summary>Quantas vezes cada chave foi transmitida; o teste de reenvio confere que nao houve duplicidade.</summary>
    public static readonly ConcurrentDictionary<string, int> Transmissoes = new();
    public static readonly ConcurrentDictionary<string, int> Consultas = new();

    public static int StatusServicoCStat = 107;
    public static readonly ConcurrentDictionary<string, DestinoSefaz> DestinosPorChave = new();

    public Task<StatusServicoSefaz> ConsultarStatusServicoAsync(DestinoSefaz destino, X509Certificate2 certificado, CancellationToken cancellationToken) =>
        Task.FromResult(new StatusServicoSefaz(StatusServicoCStat, StatusServicoCStat == 107 ? "Servico em Operacao" : "Servico Paralisado", DateTimeOffset.Now, 1));

    public Task<RetornoAutorizacao> AutorizarAsync(DestinoSefaz destino, X509Certificate2 certificado, string xmlNfeAssinado, long idLote, CancellationToken cancellationToken)
    {
        var nfe = XElement.Parse(xmlNfeAssinado);
        var chave = nfe.Element(Ns + "infNFe")!.Attribute("Id")!.Value[3..];
        var primeiroProduto = nfe.Descendants(Ns + "cProd").First().Value;
        var vezes = Transmissoes.AddOrUpdate(chave, 1, (_, n) => n + 1);
        DestinosPorChave[chave] = destino;

        switch (primeiroProduto)
        {
            case "REJEITAR":
                return Task.FromResult(new RetornoAutorizacao(104, "Lote processado", null, Protocolo(chave, 778, "Rejeicao: NCM inexistente na tabela de NCM")));

            case "SEM-RESPOSTA" when vezes == 1:
                Autorizadas[chave] = Protocolo(chave, 100, "Autorizado o uso da NF-e");
                throw new SefazIndisponivelException("A SEFAZ não respondeu (simulado).");

            case "LOTE-103" when vezes == 1:
                Autorizadas[chave] = Protocolo(chave, 100, "Autorizado o uso da NF-e");
                var recibo = "433" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString();
                Recibos[recibo] = chave;
                return Task.FromResult(new RetornoAutorizacao(103, "Lote recebido com sucesso", recibo, null));

            default:
                if (Autorizadas.ContainsKey(chave))
                    return Task.FromResult(new RetornoAutorizacao(104, "Lote processado", null, Protocolo(chave, 539, "Rejeicao: Duplicidade de NF-e")));

                var protocolo = Protocolo(chave, 100, "Autorizado o uso da NF-e");
                Autorizadas[chave] = protocolo;
                return Task.FromResult(new RetornoAutorizacao(104, "Lote processado", null, protocolo));
        }
    }

    /// <summary>Eventos: justificativa contendo "REJEITAR" e rejeitada (573); "SEM-RESPOSTA" cai na rede uma vez; o resto registra (135).</summary>
    public Task<RetornoEvento> EnviarEventoAsync(DestinoSefaz destino, X509Certificate2 certificado, string xmlEventoAssinado, long idLote, CancellationToken cancellationToken)
    {
        var evento = XElement.Parse(xmlEventoAssinado);
        var infEvento = evento.Element(Ns + "infEvento")!;
        var chave = infEvento.Element(Ns + "chNFe")!.Value;
        var tipo = infEvento.Element(Ns + "tpEvento")!.Value;
        var sequencia = infEvento.Element(Ns + "nSeqEvento")!.Value;
        var texto = infEvento.Descendants().FirstOrDefault(e => e.Name.LocalName is "xJust" or "xCorrecao")?.Value ?? string.Empty;
        var id = $"{chave}:{tipo}:{sequencia}";
        var vezes = EventosEnviados.AddOrUpdate(id, 1, (_, n) => n + 1);

        if (texto.Contains("SEM-RESPOSTA") && vezes == 1)
            throw new SefazIndisponivelException("A SEFAZ não respondeu ao evento (simulado).");

        var (cStat, xMotivo, protocolo) = texto.Contains("REJEITAR")
            ? (573, "Rejeicao: Duplicidade de evento", (string?)null)
            : (135, "Evento registrado e vinculado a NF-e", "333" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString());

        var retEvento = new XElement(Ns + "retEvento", new XAttribute("versao", "1.00"),
            new XElement(Ns + "infEvento",
                new XElement(Ns + "tpAmb", 2), new XElement(Ns + "verAplic", "FAKE"), new XElement(Ns + "cOrgao", 33),
                new XElement(Ns + "cStat", cStat), new XElement(Ns + "xMotivo", xMotivo), new XElement(Ns + "chNFe", chave),
                new XElement(Ns + "tpEvento", tipo), new XElement(Ns + "nSeqEvento", sequencia),
                new XElement(Ns + "dhRegEvento", DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")),
                protocolo is null ? null : new XElement(Ns + "nProt", protocolo)));

        return Task.FromResult(new RetornoEvento(128, "Lote de Evento Processado", cStat, xMotivo, protocolo, DateTimeOffset.Now, retEvento.ToString(SaveOptions.DisableFormatting)));
    }

    public static readonly ConcurrentDictionary<string, int> EventosEnviados = new();

    /// <summary>Inutilizacao: justificativa com "REJEITAR" devolve 241; o resto homologa (102).</summary>
    public Task<RetornoInutilizacao> InutilizarAsync(DestinoSefaz destino, X509Certificate2 certificado, string xmlInutAssinado, CancellationToken cancellationToken)
    {
        var infInut = XElement.Parse(xmlInutAssinado).Element(Ns + "infInut")!;
        var rejeitar = infInut.Element(Ns + "xJust")!.Value.Contains("REJEITAR");
        var (cStat, xMotivo, protocolo) = rejeitar
            ? (241, "Rejeicao: Um numero da faixa ja foi utilizado", (string?)null)
            : (102, "Inutilizacao de numero homologado", "333" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString());

        var retInut = new XElement(Ns + "retInutNFe", new XAttribute("versao", "4.00"),
            new XElement(Ns + "infInut",
                new XElement(Ns + "tpAmb", 2), new XElement(Ns + "verAplic", "FAKE"), new XElement(Ns + "cStat", cStat), new XElement(Ns + "xMotivo", xMotivo),
                new XElement(Ns + "cUF", 33), new XElement(Ns + "ano", infInut.Element(Ns + "ano")!.Value), new XElement(Ns + "CNPJ", infInut.Element(Ns + "CNPJ")!.Value),
                new XElement(Ns + "mod", 55), new XElement(Ns + "serie", infInut.Element(Ns + "serie")!.Value),
                new XElement(Ns + "nNFIni", infInut.Element(Ns + "nNFIni")!.Value), new XElement(Ns + "nNFFin", infInut.Element(Ns + "nNFFin")!.Value),
                new XElement(Ns + "dhRecbto", DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz")),
                protocolo is null ? null : new XElement(Ns + "nProt", protocolo)));

        return Task.FromResult(new RetornoInutilizacao(cStat, xMotivo, protocolo, DateTimeOffset.Now, retInut.ToString(SaveOptions.DisableFormatting)));
    }

    private static readonly ConcurrentDictionary<string, string> Recibos = new();
    public static readonly ConcurrentDictionary<string, int> ConsultasDeRecibo = new();

    /// <summary>Primeira consulta do recibo: lote ainda em processamento (105); depois, processado (104) com o protocolo.</summary>
    public Task<RetornoRecibo> ConsultarReciboAsync(DestinoSefaz destino, X509Certificate2 certificado, string recibo, CancellationToken cancellationToken)
    {
        var vezes = ConsultasDeRecibo.AddOrUpdate(recibo, 1, (_, n) => n + 1);

        if (!Recibos.TryGetValue(recibo, out var chave))
            return Task.FromResult(new RetornoRecibo(656, "Rejeicao: Consumo indevido", null));

        if (vezes == 1)
            return Task.FromResult(new RetornoRecibo(105, "Lote em processamento", null));

        return Task.FromResult(new RetornoRecibo(104, "Lote processado", Autorizadas[chave]));
    }

    public Task<RetornoConsulta> ConsultarPorChaveAsync(DestinoSefaz destino, X509Certificate2 certificado, ChaveAcesso chave, CancellationToken cancellationToken)
    {
        Consultas.AddOrUpdate(chave.Valor, 1, (_, n) => n + 1);

        return Task.FromResult(Autorizadas.TryGetValue(chave.Valor, out var protocolo)
            ? new RetornoConsulta(100, "Autorizado o uso da NF-e", protocolo)
            : new RetornoConsulta(217, "Rejeicao: NF-e nao consta na base de dados da SEFAZ", null));
    }

    private static ProtocoloSefaz Protocolo(string chave, int cStat, string xMotivo)
    {
        var numero = cStat == 100 ? "333" + Random.Shared.NextInt64(100_000_000_000, 999_999_999_999).ToString() : null;
        var recebimento = new DateTimeOffset(DateTime.UtcNow).ToOffset(TimeSpan.FromHours(-3));

        var protNFe = new XElement(Ns + "protNFe", new XAttribute("versao", "4.00"),
            new XElement(Ns + "infProt", new XAttribute("Id", "ID" + (numero ?? "0")),
                new XElement(Ns + "tpAmb", 2),
                new XElement(Ns + "verAplic", "FAKE"),
                new XElement(Ns + "chNFe", chave),
                new XElement(Ns + "dhRecbto", recebimento.ToString("yyyy-MM-ddTHH:mm:sszzz")),
                numero is null ? null : new XElement(Ns + "nProt", numero),
                new XElement(Ns + "cStat", cStat),
                new XElement(Ns + "xMotivo", xMotivo)));

        return new ProtocoloSefaz(cStat, xMotivo, numero, recebimento, protNFe.ToString(SaveOptions.DisableFormatting));
    }
}
