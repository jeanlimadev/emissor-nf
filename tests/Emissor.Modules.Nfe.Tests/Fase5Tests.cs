using System.Xml.Linq;
using Emissor.Contracts.Emitentes;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using Emissor.Modules.Nfe.Infrastructure.Sefaz;
using Emissor.Modules.Nfe.Infrastructure.Xml;
using Microsoft.Extensions.Options;

namespace Emissor.Modules.Nfe.Tests;

[TestClass]
public sealed class Fase5Tests
{
    private static readonly DateTimeOffset Emissao = new(2026, 10, 12, 10, 0, 0, TimeSpan.FromHours(-3));
    private static readonly XNamespace Ns = MensagensSefaz.Ns;

    [TestMethod]
    public void Chave_aceita_cnpj_alfanumerico_nas_12_primeiras_posicoes_e_calcula_dv_com_ascii()
    {
        // NT 2025.001: letras so na raiz+ordem; DVs numericos. Na chave, letra vale ASCII-48 no modulo 11.
        var chave = ChaveAcesso.Criar(33, Emissao, "12ABC345000195", 1, 7, "12345678").Value!;

        Assert.AreEqual("12ABC345000195", chave.Documento);
        StringAssert.Matches(chave.Valor, new System.Text.RegularExpressions.Regex("^[0-9]{6}[0-9A-Z]{12}[0-9]{26}$"));
        Assert.AreEqual(ChaveAcesso.CalcularDigitoVerificador(chave.Valor[..43]), chave.DigitoVerificador);
        Assert.IsTrue(ChaveAcesso.Parse(chave.Valor).IsSuccess);

        Assert.AreEqual("nfe.chave_documento_invalido", ChaveAcesso.Criar(33, Emissao, "12abc345000195", 1, 7, "12345678").Error!.Code, "minusculas nao");
        Assert.AreEqual("nfe.chave_documento_invalido", ChaveAcesso.Criar(33, Emissao, "1234567800019A", 1, 7, "12345678").Error!.Code, "DV com letra nao");
    }

    [TestMethod]
    public void Dv_com_letra_usa_ascii_menos_48()
    {
        // 'A' vale 17: a diferenca para o digito '0' na mesma posicao e 17 * peso.
        var comZero = "3326100000000000000055001000000001112345678";
        var comLetra = "332610A000000000000055001000000001112345678";
        var pesoDaPosicao6 = 2 + ((43 - 1 - 6) % 8); // pesos 2..9 da direita para a esquerda
        var esperado = (SomaPonderada(comZero) + 17 * pesoDaPosicao6) % 11;

        Assert.AreEqual(esperado < 2 ? 0 : 11 - esperado, ChaveAcesso.CalcularDigitoVerificador(comLetra));
    }

    private static int SomaPonderada(string chave)
    {
        var soma = 0;
        var peso = 2;

        for (var i = chave.Length - 1; i >= 0; i--)
        {
            soma += (chave[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        return soma;
    }

    [TestMethod]
    public void Enderecos_da_svc_an_por_servico_e_sem_inutilizacao()
    {
        Assert.AreEqual("https://hom.sefazvirtual.fazenda.gov.br/NFeAutorizacao4/NFeAutorizacao4.asmx", EnderecosSefaz.Url("RJ", AmbienteSefaz.Homologacao, ServicoSefaz.Autorizacao, contingencia: true).ToString());
        Assert.AreEqual("https://www.sefazvirtual.fazenda.gov.br/NFeRecepcaoEvento4/NFeRecepcaoEvento4.asmx", EnderecosSefaz.Url("RJ", AmbienteSefaz.Producao, ServicoSefaz.RecepcaoEvento, contingencia: true).ToString());
        Assert.AreEqual("https://www.sefazvirtual.fazenda.gov.br/NFeRetAutorizacao4/NFeRetAutorizacao4.asmx", EnderecosSefaz.Url("RJ", AmbienteSefaz.Producao, ServicoSefaz.RetornoAutorizacao, contingencia: true).ToString());
        Assert.ThrowsExactly<NotSupportedException>(() => EnderecosSefaz.Url("RJ", AmbienteSefaz.Producao, ServicoSefaz.Inutilizacao, contingencia: true));
        Assert.AreEqual("https://nfe.svrs.rs.gov.br/ws/NfeRetAutorizacao/NFeRetAutorizacao4.asmx", EnderecosSefaz.Url("RJ", AmbienteSefaz.Producao, ServicoSefaz.RetornoAutorizacao).ToString());
    }

    [TestMethod]
    public void ConsReciNFe_e_retConsReciNFe()
    {
        var consulta = XElement.Parse(MensagensSefaz.ConsReciNFe(AmbienteSefaz.Homologacao, "433000000000123"));
        Assert.AreEqual(Ns + "consReciNFe", consulta.Name);
        Assert.AreEqual("433000000000123", consulta.Element(Ns + "nRec")!.Value);

        var processando = MensagensSefaz.LerRetConsReciNFe(XElement.Parse("""<retConsReciNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00"><tpAmb>2</tpAmb><verAplic>x</verAplic><nRec>433000000000123</nRec><cStat>105</cStat><xMotivo>Lote em processamento</xMotivo><cUF>33</cUF></retConsReciNFe>"""));
        Assert.AreEqual(105, processando.CStat);
        Assert.IsNull(processando.Protocolo);

        var processado = MensagensSefaz.LerRetConsReciNFe(XElement.Parse("""
            <retConsReciNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00"><tpAmb>2</tpAmb><verAplic>x</verAplic><nRec>433000000000123</nRec><cStat>104</cStat><xMotivo>Lote processado</xMotivo><cUF>33</cUF>
              <protNFe versao="4.00"><infProt><tpAmb>2</tpAmb><verAplic>x</verAplic><chNFe>33261059020494000119550010000000011264309676</chNFe><dhRecbto>2026-10-12T10:00:01-03:00</dhRecbto><nProt>333260000000001</nProt><digVal>a=</digVal><cStat>100</cStat><xMotivo>Autorizado o uso da NF-e</xMotivo></infProt></protNFe>
            </retConsReciNFe>
            """));
        Assert.AreEqual(104, processado.CStat);
        Assert.AreEqual(100, processado.Protocolo!.CStat);
        Assert.AreEqual("333260000000001", processado.Protocolo.Numero);
    }

    [TestMethod]
    public void Contingencia_poe_tpEmis_6_dhCont_e_xJust_e_o_xml_continua_valido()
    {
        var emitente = PedidosDeTeste.EmitentePj();
        Assert.IsTrue(emitente.AtivarContingencia("SVRS indisponivel desde as 9h, venda nao pode esperar.", Emissao).IsSuccess);
        var pedido = PedidosDeTeste.PedidoSimples();
        var chave = ChaveAcesso.Criar(33, Emissao, emitente.Documento, 1, 5, "87654321", ChaveAcesso.TipoEmissaoSvcAn).Value!;

        var xml = new DfeNetNfeXmlBuilder().Montar(new NfeMontagem(emitente, chave, Emissao, pedido));
        Assert.IsTrue(xml.IsSuccess, xml.Error?.Message);

        var ide = XDocument.Parse(xml.Value!).Root!.Element(Ns + "infNFe")!.Element(Ns + "ide")!;
        Assert.AreEqual("6", ide.Element(Ns + "tpEmis")!.Value);
        Assert.AreEqual("6", chave.TipoEmissao.ToString());
        StringAssert.StartsWith(ide.Element(Ns + "dhCont")!.Value, "2026-10-12T10:00:00-03:00");
        Assert.AreEqual("SVRS indisponivel desde as 9h, venda nao pode esperar.", ide.Element(Ns + "xJust")!.Value);

        Assert.IsTrue(DestinoSefaz.Do(emitente).Contingencia);
        emitente.DesativarContingencia(Emissao);
        Assert.IsFalse(DestinoSefaz.Do(emitente).Contingencia);
        Assert.AreEqual("emitente.justificativa_contingencia_invalida", emitente.AtivarContingencia("curta", Emissao).Error!.Code);
    }

    [TestMethod]
    public void Responsavel_tecnico_entra_no_xml_quando_configurado()
    {
        var opcoes = Options.Create(new ResponsavelTecnicoOptions { Cnpj = "11.222.333/0001-81", Contato = "Jean Lima", Email = "suporte@exemplo.com.br", Telefone = "(21) 99999-0000" });
        var emitente = PedidosDeTeste.EmitentePf();
        var chave = ChaveAcesso.Criar(33, Emissao, emitente.Documento, 1, 5, "87654321").Value!;

        var xml = new DfeNetNfeXmlBuilder(opcoes).Montar(new NfeMontagem(emitente, chave, Emissao, PedidosDeTeste.PedidoRegimeNormal()));
        Assert.IsTrue(xml.IsSuccess, xml.Error?.Message);

        var resp = XDocument.Parse(xml.Value!).Root!.Element(Ns + "infNFe")!.Element(Ns + "infRespTec")!;
        Assert.AreEqual("11222333000181", resp.Element(Ns + "CNPJ")!.Value);
        Assert.AreEqual("21999990000", resp.Element(Ns + "fone")!.Value);

        var semResp = new DfeNetNfeXmlBuilder().Montar(new NfeMontagem(emitente, chave, Emissao, PedidosDeTeste.PedidoRegimeNormal())).Value!;
        Assert.IsNull(XDocument.Parse(semResp).Root!.Element(Ns + "infNFe")!.Element(Ns + "infRespTec"));
    }
}
