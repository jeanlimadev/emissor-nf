using System.Xml.Linq;
using Emissor.Contracts.Emitentes;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using Emissor.Modules.Nfe.Infrastructure.Sefaz;

namespace Emissor.Modules.Nfe.Tests;

/// <summary>As mensagens de ida e os retornos descritos no Manual de Orientacao do Contribuinte.</summary>
[TestClass]
public sealed class MensagensSefazTests
{
    private const string Chave = "33261000012345678909550010000000011080676244";
    private static readonly XNamespace Ns = MensagensSefaz.Ns;

    [TestMethod]
    public void ConsStatServ_tem_versao_ambiente_uf_e_servico()
    {
        var xml = XElement.Parse(MensagensSefaz.ConsStatServ(33, AmbienteSefaz.Homologacao));

        Assert.AreEqual(Ns + "consStatServ", xml.Name);
        Assert.AreEqual("4.00", xml.Attribute("versao")!.Value);
        Assert.AreEqual("2", xml.Element(Ns + "tpAmb")!.Value);
        Assert.AreEqual("33", xml.Element(Ns + "cUF")!.Value);
        Assert.AreEqual("STATUS", xml.Element(Ns + "xServ")!.Value);
    }

    [TestMethod]
    public void EnviNFe_embute_a_nota_sem_alterar_um_byte_e_pede_sincrono()
    {
        const string nfe = """<NFe xmlns="http://www.portalfiscal.inf.br/nfe"><infNFe versao="4.00" Id="NFe33261000012345678909550010000000011080676244"><ide/></infNFe></NFe>""";

        var envio = MensagensSefaz.EnviNFe(42, nfe);

        StringAssert.Contains(envio, nfe);
        var xml = XElement.Parse(envio);
        Assert.AreEqual("000000000000042", xml.Element(Ns + "idLote")!.Value);
        Assert.AreEqual("1", xml.Element(Ns + "indSinc")!.Value);
        Assert.IsNotNull(xml.Element(Ns + "NFe"));
    }

    [TestMethod]
    public void Envelope_soap12_coloca_a_mensagem_no_namespace_do_servico()
    {
        var envelope = XDocument.Parse(MensagensSefaz.Envelope(ServicoSefaz.Autorizacao, MensagensSefaz.ConsSitNFe(AmbienteSefaz.Producao, ChaveAcesso.Parse(Chave).Value!)));

        var dados = envelope.Descendants().Single(e => e.Name.LocalName == "nfeDadosMsg");
        Assert.AreEqual("http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4", dados.Name.NamespaceName);
        Assert.AreEqual(Ns + "consSitNFe", dados.Elements().Single().Name);
    }

    [TestMethod]
    public void Le_retEnviNFe_sincrono_com_protocolo_de_autorizacao()
    {
        var resposta = Envelope("NFeAutorizacao4", $"""
            <retEnviNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <tpAmb>2</tpAmb><verAplic>SVRS202609</verAplic><cStat>104</cStat><xMotivo>Lote processado</xMotivo><cUF>33</cUF>
              <dhRecbto>2026-10-12T10:00:00-03:00</dhRecbto>
              <protNFe versao="4.00"><infProt Id="ID333260000000001"><tpAmb>2</tpAmb><verAplic>SVRS202609</verAplic>
                <chNFe>{Chave}</chNFe><dhRecbto>2026-10-12T10:00:01-03:00</dhRecbto><nProt>333260000000001</nProt>
                <digVal>abc=</digVal><cStat>100</cStat><xMotivo>Autorizado o uso da NF-e</xMotivo></infProt></protNFe>
            </retEnviNFe>
            """);

        var retorno = MensagensSefaz.LerRetEnviNFe(MensagensSefaz.ExtrairResultado(resposta));

        Assert.AreEqual(104, retorno.CStat);
        Assert.IsNull(retorno.Recibo);
        Assert.AreEqual(100, retorno.Protocolo!.CStat);
        Assert.AreEqual("333260000000001", retorno.Protocolo.Numero);
        Assert.AreEqual(new DateTimeOffset(2026, 10, 12, 10, 0, 1, TimeSpan.FromHours(-3)), retorno.Protocolo.Recebimento);
        StringAssert.StartsWith(retorno.Protocolo.XmlProtNfe, "<protNFe");
        StringAssert.Contains(retorno.Protocolo.XmlProtNfe, "<nProt>333260000000001</nProt>");
    }

    [TestMethod]
    public void Le_retEnviNFe_assincrono_com_recibo()
    {
        var resposta = Envelope("NFeAutorizacao4", """
            <retEnviNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <tpAmb>2</tpAmb><verAplic>SVRS</verAplic><cStat>103</cStat><xMotivo>Lote recebido com sucesso</xMotivo><cUF>33</cUF>
              <dhRecbto>2026-10-12T10:00:00-03:00</dhRecbto><infRec><nRec>433000000000123</nRec><tMed>1</tMed></infRec>
            </retEnviNFe>
            """);

        var retorno = MensagensSefaz.LerRetEnviNFe(MensagensSefaz.ExtrairResultado(resposta));

        Assert.AreEqual(103, retorno.CStat);
        Assert.AreEqual("433000000000123", retorno.Recibo);
        Assert.IsNull(retorno.Protocolo);
    }

    [TestMethod]
    public void Le_retConsSitNFe_e_retConsStatServ()
    {
        var consulta = MensagensSefaz.LerRetConsSitNFe(MensagensSefaz.ExtrairResultado(Envelope("NFeConsultaProtocolo4", """
            <retConsSitNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <tpAmb>2</tpAmb><verAplic>SVRS</verAplic><cStat>217</cStat><xMotivo>Rejeicao: NF-e nao consta na base de dados da SEFAZ</xMotivo><cUF>33</cUF>
            </retConsSitNFe>
            """)));
        Assert.AreEqual(217, consulta.CStat);
        Assert.IsNull(consulta.Protocolo);

        var status = MensagensSefaz.LerRetConsStatServ(MensagensSefaz.ExtrairResultado(Envelope("NFeStatusServico4", """
            <retConsStatServ xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00">
              <tpAmb>2</tpAmb><verAplic>SVRS</verAplic><cStat>107</cStat><xMotivo>Servico em Operacao</xMotivo><cUF>33</cUF>
              <dhRecbto>2026-10-12T10:00:00-03:00</dhRecbto><tMed>1</tMed>
            </retConsStatServ>
            """)));
        Assert.AreEqual(107, status.CStat);
        Assert.AreEqual(1, status.TempoMedioSegundos);
    }

    [TestMethod]
    public void Falha_soap_e_resposta_estranha_viram_sefaz_indisponivel()
    {
        var falha = """
            <soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"><soap:Body><soap:Fault>
              <soap:Code><soap:Value>soap:Receiver</soap:Value></soap:Code>
              <soap:Reason><soap:Text xml:lang="pt">Erro interno do servidor</soap:Text></soap:Reason>
            </soap:Fault></soap:Body></soap:Envelope>
            """;
        var ex = Assert.ThrowsExactly<SefazIndisponivelException>(() => MensagensSefaz.ExtrairResultado(falha));
        StringAssert.Contains(ex.Message, "Erro interno do servidor");

        Assert.ThrowsExactly<SefazIndisponivelException>(() => MensagensSefaz.ExtrairResultado("<html>manutencao</html>"));
        Assert.ThrowsExactly<SefazIndisponivelException>(() => MensagensSefaz.ExtrairResultado("isso nao e xml"));
    }

    [TestMethod]
    public void ProcNFe_junta_nota_e_protocolo_no_namespace_da_nfe()
    {
        var proc = XElement.Parse(MensagensSefaz.ProcNFe("""<NFe xmlns="http://www.portalfiscal.inf.br/nfe"><infNFe/></NFe>""", """<protNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00"><infProt/></protNFe>"""));

        Assert.AreEqual(Ns + "nfeProc", proc.Name);
        Assert.AreEqual("4.00", proc.Attribute("versao")!.Value);
        Assert.AreEqual(Ns + "NFe", proc.Elements().First().Name);
        Assert.AreEqual(Ns + "protNFe", proc.Elements().Last().Name);
    }

    [TestMethod]
    public void Enderecos_do_svrs_por_ambiente_e_servico()
    {
        Assert.AreEqual("SVRS", EnderecosSefaz.Autorizador("RJ"));
        Assert.AreEqual("https://nfe-homologacao.svrs.rs.gov.br/ws/NfeAutorizacao/NFeAutorizacao4.asmx", EnderecosSefaz.Url("RJ", AmbienteSefaz.Homologacao, ServicoSefaz.Autorizacao).ToString());
        Assert.AreEqual("https://nfe.svrs.rs.gov.br/ws/NfeStatusServico/NfeStatusServico4.asmx", EnderecosSefaz.Url("RJ", AmbienteSefaz.Producao, ServicoSefaz.StatusServico).ToString());
        Assert.AreEqual("https://nfe.svrs.rs.gov.br/ws/NfeConsulta/NfeConsulta4.asmx", EnderecosSefaz.Url("ES", AmbienteSefaz.Producao, ServicoSefaz.ConsultaProtocolo).ToString());
        Assert.AreEqual("http://www.portalfiscal.inf.br/nfe/wsdl/NFeAutorizacao4/nfeAutorizacaoLote", EnderecosSefaz.Action(ServicoSefaz.Autorizacao));
        Assert.AreEqual("http://www.portalfiscal.inf.br/nfe/wsdl/NFeStatusServico4/nfeStatusServicoNF", EnderecosSefaz.Action(ServicoSefaz.StatusServico));
        Assert.AreEqual("http://www.portalfiscal.inf.br/nfe/wsdl/NFeConsultaProtocolo4/nfeConsultaNF", EnderecosSefaz.Action(ServicoSefaz.ConsultaProtocolo));
        Assert.ThrowsExactly<NotSupportedException>(() => EnderecosSefaz.Url("SP", AmbienteSefaz.Producao, ServicoSefaz.Autorizacao));
    }

    private static string Envelope(string servico, string resultado) =>
        $"""<?xml version="1.0" encoding="utf-8"?><soap:Envelope xmlns:soap="http://www.w3.org/2003/05/soap-envelope"><soap:Body><nfeResultMsg xmlns="http://www.portalfiscal.inf.br/nfe/wsdl/{servico}">{resultado}</nfeResultMsg></soap:Body></soap:Envelope>""";
}
