using Emissor.Contracts.Emitentes;

namespace Emissor.Modules.Nfe.Infrastructure.Sefaz;

/// <summary>Os servicos da NF-e que o emissor chama (versao 4.00 de todos).</summary>
public enum ServicoSefaz
{
    StatusServico,
    Autorizacao,
    RetornoAutorizacao,
    ConsultaProtocolo,
    Inutilizacao,
    RecepcaoEvento
}

/// <summary>
/// Onde fica cada webservice. E a unica tabela que muda quando a SEFAZ troca
/// um endereco: mantenha-a conferida com o Portal da NF-e (menu Servicos >
/// Relacao de servicos). Por ora so o SVRS (Sefaz Virtual do RS), que atende
/// o RJ e mais uma dezena de UFs; outros autorizadores entram aqui quando um
/// emitente de outra UF aparecer.
/// </summary>
public static class EnderecosSefaz
{
    public const string NamespaceWsdl = "http://www.portalfiscal.inf.br/nfe/wsdl/";

    /// <summary>UFs cuja NF-e e autorizada pelo SVRS (conferido em 12/10/2026 com a tabela da DFe.NET).</summary>
    public static readonly IReadOnlySet<string> UfsDoSvrs = new HashSet<string>(StringComparer.Ordinal)
    {
        "AC", "AL", "AP", "DF", "ES", "PA", "PB", "PI", "RJ", "RN", "RO", "RR", "RS", "SC", "SE", "TO"
    };

    private static readonly Dictionary<ServicoSefaz, string> CaminhosSvrs = new()
    {
        [ServicoSefaz.StatusServico] = "/ws/NfeStatusServico/NfeStatusServico4.asmx",
        [ServicoSefaz.Autorizacao] = "/ws/NfeAutorizacao/NFeAutorizacao4.asmx",
        [ServicoSefaz.RetornoAutorizacao] = "/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx",
        [ServicoSefaz.ConsultaProtocolo] = "/ws/NfeConsulta/NfeConsulta4.asmx",
        [ServicoSefaz.Inutilizacao] = "/ws/nfeinutilizacao/nfeinutilizacao4.asmx",
        [ServicoSefaz.RecepcaoEvento] = "/ws/recepcaoevento/recepcaoevento4.asmx"
    };

    /// <summary>Namespace do WSDL e nome da operacao SOAP de cada servico (vao na action da chamada).</summary>
    private static readonly Dictionary<ServicoSefaz, (string Servico, string Operacao)> Operacoes = new()
    {
        [ServicoSefaz.StatusServico] = ("NFeStatusServico4", "nfeStatusServicoNF"),
        [ServicoSefaz.Autorizacao] = ("NFeAutorizacao4", "nfeAutorizacaoLote"),
        [ServicoSefaz.RetornoAutorizacao] = ("NFeRetAutorizacao4", "nfeRetAutorizacaoLote"),
        [ServicoSefaz.ConsultaProtocolo] = ("NFeConsultaProtocolo4", "nfeConsultaNF"),
        [ServicoSefaz.Inutilizacao] = ("NFeInutilizacao4", "nfeInutilizacaoNF"),
        [ServicoSefaz.RecepcaoEvento] = ("NFeRecepcaoEvento4", "nfeRecepcaoEvento")
    };

    public static string Autorizador(string uf) =>
        UfsDoSvrs.Contains(uf) ? "SVRS" : throw new NotSupportedException($"A UF {uf} ainda não tem autorizador configurado no emissor.");

    public static Uri Url(string uf, AmbienteSefaz ambiente, ServicoSefaz servico, bool contingencia = false)
    {
        _ = Autorizador(uf);

        if (contingencia)
            return UrlSvcAn(ambiente, servico);

        var host = ambiente == AmbienteSefaz.Producao
            ? "https://nfe.svrs.rs.gov.br"
            : "https://nfe-homologacao.svrs.rs.gov.br";

        return new Uri(host + CaminhosSvrs[servico]);
    }

    /// <summary>
    /// SVC-AN: Sefaz Virtual de Contingencia do Ambiente Nacional, usada pelas
    /// UFs do SVRS quando ele cai. Nao tem inutilizacao: contingencia e so
    /// para autorizar, consultar e registrar eventos.
    /// </summary>
    public static Uri UrlSvcAn(AmbienteSefaz ambiente, ServicoSefaz servico)
    {
        if (servico == ServicoSefaz.Inutilizacao)
            throw new NotSupportedException("A SVC-AN não oferece inutilização; desligue a contingência para inutilizar.");

        var host = ambiente == AmbienteSefaz.Producao
            ? "https://www.sefazvirtual.fazenda.gov.br"
            : "https://hom.sefazvirtual.fazenda.gov.br";
        var nome = Operacoes[servico].Servico;

        return new Uri($"{host}/{nome}/{nome}.asmx");
    }

    /// <summary>Namespace do elemento <c>nfeDadosMsg</c>/<c>nfeResultMsg</c> no envelope SOAP.</summary>
    public static string NamespaceServico(ServicoSefaz servico) => NamespaceWsdl + Operacoes[servico].Servico;

    /// <summary>A action SOAP 1.2: namespace do servico + nome da operacao.</summary>
    public static string Action(ServicoSefaz servico) => NamespaceServico(servico) + "/" + Operacoes[servico].Operacao;
}
