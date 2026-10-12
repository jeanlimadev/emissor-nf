using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Tests;
using Emissor.Modules.Nfe.Domain;
using Emissor.Modules.Nfe.Infrastructure.Assinatura;
using Emissor.Modules.Nfe.Infrastructure.Sefaz;
using Emissor.Modules.Nfe.Infrastructure.Xml;

namespace Emissor.Modules.Nfe.Tests;

[TestClass]
public sealed class EventosTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 12, 15, 0, 0, TimeSpan.Zero);
    private static readonly XNamespace Ns = MensagensSefaz.Ns;
    private const string Chave = "33261059020494000119550010000000011264309676";

    private static NotaFiscal NotaAutorizada(DateTimeOffset autorizadaEm)
    {
        var nota = NotaFiscal.Receber(Guid.NewGuid(), Guid.NewGuid(), AmbienteSefaz.Homologacao, 1, "k", "h", "{}", autorizadaEm);
        nota.IniciarProcessamento(autorizadaEm);
        nota.DefinirNumeroEChave(1, ChaveAcesso.Parse(Chave).Value!, autorizadaEm, autorizadaEm);
        nota.DefinirXmlAssinado("<NFe/>", autorizadaEm);
        nota.RegistrarAutorizacao(new ProtocoloSefaz(100, "Autorizado", "333260000000001", autorizadaEm, "<protNFe/>"), "<nfeProc/>", autorizadaEm);
        return nota;
    }

    [TestMethod]
    public void Cancelamento_exige_nota_autorizada_prazo_de_24h_e_justificativa()
    {
        var nota = NotaAutorizada(Agora.AddHours(-2));

        Assert.AreEqual("nfe.justificativa_invalida", EventoNfe.Cancelamento(nota, "curta", Agora).Error!.Code);
        Assert.AreEqual("nfe.cancelamento_fora_do_prazo", EventoNfe.Cancelamento(nota, "Erro no pedido do cliente, nota refeita.", Agora.AddHours(23)).Error!.Code);

        var evento = EventoNfe.Cancelamento(nota, "  Erro no   pedido do cliente, nota refeita. ", Agora).Value!;
        Assert.AreEqual(TipoEventoNfe.Cancelamento, evento.Tipo);
        Assert.AreEqual(1, evento.Sequencia);
        Assert.AreEqual("Erro no pedido do cliente, nota refeita.", evento.Texto, "espacos normalizados");
        Assert.AreEqual($"ID110111{Chave}01", evento.IdInfEvento(Chave));

        var rejeitada = NotaFiscal.Receber(Guid.NewGuid(), Guid.NewGuid(), AmbienteSefaz.Homologacao, 1, "k", "h", "{}", Agora);
        rejeitada.IniciarProcessamento(Agora);
        rejeitada.RegistrarRejeicao(778, "NCM", Agora);
        Assert.AreEqual("nfe.cancelamento_so_de_autorizada", EventoNfe.Cancelamento(rejeitada, "Erro no pedido do cliente, nota refeita.", Agora).Error!.Code);
    }

    [TestMethod]
    public void Registro_do_cancelamento_cancela_a_nota()
    {
        var nota = NotaAutorizada(Agora.AddHours(-1));
        var evento = EventoNfe.Cancelamento(nota, "Erro no pedido do cliente, nota refeita.", Agora).Value!;

        Assert.AreEqual("nfe.cancelamento_nao_registrado", nota.RegistrarCancelamento(evento, Agora).Error!.Code, "pendente nao cancela");

        evento.RegistrarRetorno(135, "Evento registrado e vinculado a NF-e", "333260000000099", Agora, "<procEventoNFe/>");
        Assert.AreEqual(EventoStatus.Registrado, evento.Status);
        Assert.IsTrue(nota.RegistrarCancelamento(evento, Agora).IsSuccess);
        Assert.AreEqual(NfeStatus.Cancelada, nota.Status);
        Assert.IsTrue(nota.EhFinal);

        var outro = EventoNfe.CartaCorrecao(nota, "Correcao qualquer de teste.", 0, Agora);
        Assert.AreEqual("nfe.carta_so_de_autorizada", outro.Error!.Code, "cancelada nao recebe carta");
    }

    [TestMethod]
    public void Carta_de_correcao_sequencia_limite_e_rejeicao()
    {
        var nota = NotaAutorizada(Agora.AddDays(-10));

        var primeira = EventoNfe.CartaCorrecao(nota, "Corrige o endereco de entrega.", 0, Agora).Value!;
        Assert.AreEqual(1, primeira.Sequencia);
        Assert.AreEqual($"ID110110{Chave}01", primeira.IdInfEvento(Chave));
        Assert.AreEqual(3, EventoNfe.CartaCorrecao(nota, "Corrige o endereco de entrega.", 2, Agora).Value!.Sequencia);
        Assert.AreEqual("nfe.carta_limite_atingido", EventoNfe.CartaCorrecao(nota, "Corrige o endereco de entrega.", 20, Agora).Error!.Code);
        Assert.AreEqual("nfe.correcao_invalida", EventoNfe.CartaCorrecao(nota, "curta", 0, Agora).Error!.Code);

        primeira.RegistrarRetorno(573, "Rejeicao: Duplicidade de evento", null, null, null);
        Assert.AreEqual(EventoStatus.Rejeitado, primeira.Status);
    }

    [TestMethod]
    public void Xml_do_evento_assinado_valida_no_schema_oficial()
    {
        var emitente = PedidosDeTeste.EmitentePj();
        var nota = NotaAutorizada(Agora.AddHours(-1));
        var evento = EventoNfe.Cancelamento(nota, "Erro no pedido do cliente, nota refeita.", Agora).Value!;
        var carta = EventoNfe.CartaCorrecao(nota, "Corrige o endereco de entrega para a rua certa.", 0, Agora).Value!;
        using var certificado = X509CertificateLoader.LoadPkcs12(CertificadoDeTeste.PfxComCn($"X:{emitente.Documento}"), CertificadoDeTeste.Senha, X509KeyStorageFlags.EphemeralKeySet);
        var assinador = new XmlDsigNfeAssinador();
        var xsd = new NfeXsdValidator();

        foreach (var (e, tipo) in new[] { (evento, TipoEventoNfe.Cancelamento), (carta, TipoEventoNfe.CartaCorrecao) })
        {
            var xml = assinador.AssinarElemento(MensagensEventos.Evento(e, nota, emitente, Chave, Uf.NoFuso(Agora, "RJ")), "infEvento", certificado);
            var doc = XElement.Parse(xml);
            Assert.AreEqual(Ns + "evento", doc.Name);
            Assert.AreEqual(Ns + "infEvento", doc.Elements().First().Name);
            Assert.AreEqual("Signature", doc.Elements().Last().Name.LocalName, "assinatura depois do infEvento");
            Assert.AreEqual("33", doc.Element(Ns + "infEvento")!.Element(Ns + "cOrgao")!.Value);
            Assert.AreEqual(emitente.Documento, doc.Element(Ns + "infEvento")!.Element(Ns + "CNPJ")!.Value);
            Assert.IsTrue(XmlDsigNfeAssinador.Verificar(xml));

            var validacao = xsd.ValidarEvento(MensagensEventos.EnvEvento(1, xml), tipo);
            Assert.IsTrue(validacao.IsSuccess, string.Join(" | ", validacao.Error?.Details?["xml"] ?? []));
        }
    }

    [TestMethod]
    public void Le_retEnvEvento_e_monta_procEventoNFe()
    {
        var resposta = XElement.Parse($"""
            <retEnvEvento xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.00">
              <idLote>1</idLote><tpAmb>2</tpAmb><verAplic>SVRS</verAplic><cOrgao>33</cOrgao><cStat>128</cStat><xMotivo>Lote de Evento Processado</xMotivo>
              <retEvento versao="1.00"><infEvento><tpAmb>2</tpAmb><verAplic>SVRS</verAplic><cOrgao>33</cOrgao><cStat>135</cStat>
                <xMotivo>Evento registrado e vinculado a NF-e</xMotivo><chNFe>{Chave}</chNFe><tpEvento>110111</tpEvento><xEvento>Cancelamento</xEvento>
                <nSeqEvento>1</nSeqEvento><dhRegEvento>2026-10-12T12:00:00-03:00</dhRegEvento><nProt>333260000000099</nProt></infEvento></retEvento>
            </retEnvEvento>
            """);

        var retorno = MensagensEventos.LerRetEnvEvento(resposta);

        Assert.AreEqual(128, retorno.CStatLote);
        Assert.AreEqual(135, retorno.CStat);
        Assert.AreEqual("333260000000099", retorno.Protocolo);
        StringAssert.StartsWith(retorno.XmlRetEvento, "<retEvento");

        var proc = XElement.Parse(MensagensEventos.ProcEventoNFe("""<evento xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.00"><infEvento/></evento>""", retorno.XmlRetEvento!));
        Assert.AreEqual(Ns + "procEventoNFe", proc.Name);
        Assert.AreEqual(Ns + "evento", proc.Elements().First().Name);
        Assert.AreEqual(Ns + "retEvento", proc.Elements().Last().Name);

        var semEvento = MensagensEventos.LerRetEnvEvento(XElement.Parse("""<retEnvEvento xmlns="http://www.portalfiscal.inf.br/nfe" versao="1.00"><idLote>1</idLote><tpAmb>2</tpAmb><verAplic>x</verAplic><cOrgao>33</cOrgao><cStat>252</cStat><xMotivo>Rejeicao: Ambiente de uso incompativel</xMotivo></retEnvEvento>"""));
        Assert.AreEqual(252, semEvento.CStatLote);
        Assert.IsNull(semEvento.CStat);
    }

    [TestMethod]
    public void Inutilizacao_regras_id_xml_e_retorno()
    {
        var emitente = PedidosDeTeste.EmitentePj();
        var pf = PedidosDeTeste.EmitentePf();

        Assert.AreEqual("nfe.inutilizacao_so_para_cnpj", Inutilizacao.Criar(pf.TenantId, pf.Id, pf.Tipo, 1, 5, 7, "Numeros pulados por falha do sistema.", Agora).Error!.Code);
        Assert.AreEqual("nfe.faixa_invalida", Inutilizacao.Criar(emitente.TenantId, emitente.Id, emitente.Tipo, 1, 7, 5, "Numeros pulados por falha do sistema.", Agora).Error!.Code);
        Assert.AreEqual("nfe.justificativa_invalida", Inutilizacao.Criar(emitente.TenantId, emitente.Id, emitente.Tipo, 1, 5, 7, "curta", Agora).Error!.Code);

        var inut = Inutilizacao.Criar(emitente.TenantId, emitente.Id, emitente.Tipo, 1, 5, 7, "Numeros pulados por falha do sistema.", Agora).Value!;
        Assert.AreEqual(26, inut.Ano);
        Assert.AreEqual("ID33261122233300018155001000000005000000007", inut.IdInfInut(33, emitente.Documento), "ID + cUF + ano + CNPJ + mod + serie + nNFIni + nNFFin = 43 caracteres");

        using var certificado = X509CertificateLoader.LoadPkcs12(CertificadoDeTeste.PfxComCn($"X:{emitente.Documento}"), CertificadoDeTeste.Senha, X509KeyStorageFlags.EphemeralKeySet);
        var xml = new XmlDsigNfeAssinador().AssinarElemento(MensagensEventos.InutNFe(inut, emitente), "infInut", certificado);
        var validacao = new NfeXsdValidator().ValidarInutilizacao(xml);
        Assert.IsTrue(validacao.IsSuccess, string.Join(" | ", validacao.Error?.Details?["xml"] ?? []));

        var retorno = MensagensEventos.LerRetInutNFe(XElement.Parse("""
            <retInutNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00"><infInut><tpAmb>2</tpAmb><verAplic>SVRS</verAplic><cStat>102</cStat>
              <xMotivo>Inutilizacao de numero homologado</xMotivo><cUF>33</cUF><ano>26</ano><CNPJ>11222333000181</CNPJ><mod>55</mod><serie>1</serie>
              <nNFIni>5</nNFIni><nNFFin>7</nNFFin><dhRecbto>2026-10-12T12:00:00-03:00</dhRecbto><nProt>333260000000077</nProt></infInut></retInutNFe>
            """));
        inut.RegistrarRetorno(retorno.CStat, retorno.XMotivo, retorno.Protocolo, retorno.Recebimento, MensagensEventos.ProcInutNFe(xml, retorno.XmlRetInut!));
        Assert.AreEqual(EventoStatus.Registrado, inut.Status);
        Assert.AreEqual("333260000000077", inut.Protocolo);
        StringAssert.StartsWith(inut.XmlProcInutilizacao, "<procInutNFe");
    }
}
