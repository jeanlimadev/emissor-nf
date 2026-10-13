using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Tests;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using Emissor.Modules.Nfe.Infrastructure.Assinatura;
using Emissor.Modules.Nfe.Infrastructure.Xml;

namespace Emissor.Modules.Nfe.Tests;

/// <summary>
/// O caminho offline inteiro, sem banco: pedido -> XML (DFe.NET) -> assinatura -> XSD.
/// Se o XML gerado nao passar no schema oficial, a SEFAZ tambem o recusaria.
/// </summary>
[TestClass]
public sealed class MontagemDeXmlTests
{
    private static readonly XNamespace Ns = NfeXsdValidator.NamespaceNfe;
    private static readonly DfeNetNfeXmlBuilder Builder = new();
    private static readonly NfeXsdValidator Xsd = new();
    private static readonly XmlDsigNfeAssinador Assinador = new();

    [TestMethod]
    public void Emitente_cpf_regime_normal_gera_xml_valido_assinado_com_ibs_cbs()
    {
        var emitente = PedidosDeTeste.EmitentePf();
        var montagem = Montagem(emitente, PedidosDeTeste.PedidoRegimeNormal());

        var xml = Builder.Montar(montagem);
        Assert.IsTrue(xml.IsSuccess, Erros(xml.Error));

        var doc = XDocument.Parse(xml.Value!);
        var infNFe = doc.Root!.Element(Ns + "infNFe")!;
        Assert.AreEqual(montagem.Chave.Id, infNFe.Attribute("Id")!.Value);
        Assert.AreEqual("4.00", infNFe.Attribute("versao")!.Value);

        var emit = infNFe.Element(Ns + "emit")!;
        Assert.AreEqual(CertificadoDeTeste.CpfValido, emit.Element(Ns + "CPF")!.Value);
        Assert.IsNull(emit.Element(Ns + "CNPJ"));
        Assert.AreEqual("3", emit.Element(Ns + "CRT")!.Value);

        var ide = infNFe.Element(Ns + "ide")!;
        Assert.AreEqual("33", ide.Element(Ns + "cUF")!.Value);
        Assert.AreEqual(montagem.Chave.CodigoNumerico, ide.Element(Ns + "cNF")!.Value);
        Assert.AreEqual(montagem.Chave.DigitoVerificador.ToString(), ide.Element(Ns + "cDV")!.Value);
        Assert.AreEqual("2", ide.Element(Ns + "tpAmb")!.Value);
        StringAssert.StartsWith(ide.Element(Ns + "dhEmi")!.Value, "2026-10-11T12:30:00-03:00");

        Assert.AreEqual(DfeNetNfeXmlBuilder.NomeDestinatarioHomologacao, infNFe.Element(Ns + "dest")!.Element(Ns + "xNome")!.Value);

        var imposto = infNFe.Element(Ns + "det")!.Element(Ns + "imposto")!;
        Assert.AreEqual("45.00", imposto.Element(Ns + "ICMS")!.Element(Ns + "ICMS00")!.Element(Ns + "vICMS")!.Value);
        var ibscbs = imposto.Element(Ns + "IBSCBS")!;
        Assert.AreEqual("000", ibscbs.Element(Ns + "CST")!.Value);
        Assert.AreEqual("250.00", ibscbs.Element(Ns + "gIBSCBS")!.Element(Ns + "vBC")!.Value);
        Assert.AreEqual("2.25", ibscbs.Element(Ns + "gIBSCBS")!.Element(Ns + "gCBS")!.Element(Ns + "vCBS")!.Value);

        var total = infNFe.Element(Ns + "total")!;
        Assert.AreEqual("250.00", total.Element(Ns + "ICMSTot")!.Element(Ns + "vNF")!.Value);
        Assert.AreEqual("45.00", total.Element(Ns + "ICMSTot")!.Element(Ns + "vICMS")!.Value);
        Assert.AreEqual("250.00", total.Element(Ns + "IBSCBSTot")!.Element(Ns + "vBCIBSCBS")!.Value);
        Assert.AreEqual("0.25", total.Element(Ns + "IBSCBSTot")!.Element(Ns + "gIBS")!.Element(Ns + "vIBS")!.Value);

        var assinado = Assinar(xml.Value!, $"PRODUTOR RURAL TESTE:{CertificadoDeTeste.CpfValido}");
        Assert.IsTrue(XmlDsigNfeAssinador.Verificar(assinado), "assinatura tem que conferir");

        var validacao = Xsd.ValidarNfe(assinado);
        Assert.IsTrue(validacao.IsSuccess, Erros(validacao.Error));
    }

    [TestMethod]
    public void Emitente_cnpj_simples_nacional_gera_xml_valido()
    {
        var emitente = PedidosDeTeste.EmitentePj();
        var montagem = Montagem(emitente, PedidosDeTeste.PedidoSimples());

        var xml = Builder.Montar(montagem);
        Assert.IsTrue(xml.IsSuccess, Erros(xml.Error));

        var doc = XDocument.Parse(xml.Value!);
        var infNFe = doc.Root!.Element(Ns + "infNFe")!;
        var emit = infNFe.Element(Ns + "emit")!;
        Assert.AreEqual(CertificadoDeTeste.CnpjValido, emit.Element(Ns + "CNPJ")!.Value);
        Assert.IsNull(emit.Element(Ns + "CPF"));
        Assert.AreEqual("1", emit.Element(Ns + "CRT")!.Value);

        var ide = infNFe.Element(Ns + "ide")!;
        Assert.AreEqual("2", ide.Element(Ns + "idDest")!.Value, "RJ para SP e interestadual");
        Assert.AreEqual("0", ide.Element(Ns + "indIntermed")!.Value, "venda pela internet informa indIntermed");

        var imposto = infNFe.Element(Ns + "det")!.Element(Ns + "imposto")!;
        Assert.AreEqual("102", imposto.Element(Ns + "ICMS")!.Element(Ns + "ICMSSN102")!.Element(Ns + "CSOSN")!.Value);
        Assert.IsNull(imposto.Element(Ns + "IBSCBS")!.Element(Ns + "gIBSCBS"), "CST 410 sem valores");
        Assert.IsNull(infNFe.Element(Ns + "total")!.Element(Ns + "IBSCBSTot"), "sem valores de IBS/CBS, sem total");

        var assinado = Assinar(xml.Value!, $"EMPRESA TESTE:{CertificadoDeTeste.CnpjValido}");
        var validacao = Xsd.ValidarNfe(assinado);
        Assert.IsTrue(validacao.IsSuccess, Erros(validacao.Error));
    }

    [TestMethod]
    public void Xml_sem_assinatura_nao_passa_no_schema()
    {
        var xml = Builder.Montar(Montagem(PedidosDeTeste.EmitentePf(), PedidosDeTeste.PedidoRegimeNormal())).Value!;

        var validacao = Xsd.ValidarNfe(xml);

        Assert.IsTrue(validacao.IsFailure);
        Assert.AreEqual("nfe.xml_invalido", validacao.Error!.Code);
        StringAssert.Contains(validacao.Error.Details!["xml"][0], "Signature");
    }

    [TestMethod]
    public void Campos_faltando_para_o_cst_viram_lista_de_erros_por_campo()
    {
        var pedido = PedidosDeTeste.PedidoRegimeNormal();
        var item = pedido.Itens[0];
        var icmsSemBase = item.Impostos.Icms with { BaseCalculo = null, Aliquota = null };
        pedido = pedido with { Itens = [item with { Impostos = item.Impostos with { Icms = icmsSemBase } }] };

        var xml = Builder.Montar(Montagem(PedidosDeTeste.EmitentePf(), pedido));

        Assert.IsTrue(xml.IsFailure);
        Assert.AreEqual("nfe.pedido_invalido", xml.Error!.Code);
        Assert.IsTrue(xml.Error.Details!.ContainsKey("itens[0].impostos.icms.baseCalculo"));
        Assert.IsTrue(xml.Error.Details.ContainsKey("itens[0].impostos.icms.aliquota"));
    }

    [TestMethod]
    public void Simples_nacional_com_cst_em_vez_de_csosn_e_erro()
    {
        var pedido = PedidosDeTeste.PedidoRegimeNormal();

        var xml = Builder.Montar(Montagem(PedidosDeTeste.EmitentePj(RegimeTributario.SimplesNacional), pedido));

        Assert.IsTrue(xml.IsFailure);
        Assert.IsTrue(xml.Error!.Details!.ContainsKey("itens[0].impostos.icms.csosn"));
    }

    [TestMethod]
    public void Ie_do_destinatario_com_digito_errado_e_erro_antes_da_sefaz()
    {
        var pedido = PedidosDeTeste.PedidoRegimeNormal();
        pedido = pedido with { Destinatario = pedido.Destinatario with { InscricaoEstadual = "99887766" } };

        var xml = Builder.Montar(Montagem(PedidosDeTeste.EmitentePf(), pedido));

        Assert.IsTrue(xml.IsFailure);
        StringAssert.Contains(xml.Error!.Details!["destinatario.inscricaoEstadual"][0], "210");
    }

    [TestMethod]
    public void Cst_de_ibs_cbs_tributado_sem_valores_e_erro_antes_da_sefaz()
    {
        // Rejeicao 1022 vista em homologacao: CST 200 com so CST e cClassTrib, sem gIBSCBS.
        var pedido = PedidosDeTeste.PedidoSimples();
        var item = pedido.Itens[0];
        var semValores = new Contracts.Nfe.IbsCbsDto("200", "200003", null, null, null, null, null);
        pedido = pedido with { Itens = [item with { Impostos = item.Impostos with { IbsCbs = semValores } }] };

        var xml = Builder.Montar(Montagem(PedidosDeTeste.EmitentePj(), pedido));

        Assert.IsTrue(xml.IsFailure);
        StringAssert.Contains(xml.Error!.Details!["itens[0].impostos.ibsCbs.baseCalculo"][0], "1022");

        // CST sem tributo (410) continua aceito so com CST e classificacao.
        Assert.IsTrue(Builder.Montar(Montagem(PedidosDeTeste.EmitentePj(), PedidosDeTeste.PedidoSimples())).IsSuccess);
    }

    [TestMethod]
    public void Pagamento_que_nao_fecha_com_o_total_e_erro()
    {
        var pedido = PedidosDeTeste.PedidoRegimeNormal();
        pedido = pedido with { Pagamento = new PagamentoDto([new DetalhePagamentoDto(null, FormaPagamento.Dinheiro, 100m, null)], null) };

        var xml = Builder.Montar(Montagem(PedidosDeTeste.EmitentePf(), pedido));

        Assert.IsTrue(xml.IsFailure);
        Assert.IsTrue(xml.Error!.Details!.ContainsKey("pagamento.detalhes"));
    }

    [TestMethod]
    public void Valor_total_do_item_divergente_de_quantidade_vezes_unitario_e_erro()
    {
        var pedido = PedidosDeTeste.PedidoRegimeNormal();
        var item = pedido.Itens[0];
        pedido = pedido with { Itens = [item with { Produto = item.Produto with { ValorTotal = 999m } }] };

        var xml = Builder.Montar(Montagem(PedidosDeTeste.EmitentePf(), pedido));

        Assert.IsTrue(xml.IsFailure);
        Assert.IsTrue(xml.Error!.Details!.ContainsKey("itens[0].produto.valorTotal"));
    }

    private static NfeMontagem Montagem(Emitentes.Domain.Emitente emitente, EmissaoNfeRequest pedido)
    {
        var dataEmissao = Uf.NoFuso(pedido.Identificacao.DataEmissao!.Value, emitente.Endereco.Uf);
        var chave = ChaveAcesso.Criar(Uf.Codigo(emitente.Endereco.Uf), dataEmissao, emitente.Documento, pedido.Identificacao.Serie, 123, "87654321").Value!;

        return new NfeMontagem(emitente, chave, dataEmissao, pedido);
    }

    private static string Assinar(string xml, string commonName)
    {
        var pfx = CertificadoDeTeste.PfxComCn(commonName);
        using var certificado = X509CertificateLoader.LoadPkcs12(pfx, CertificadoDeTeste.Senha, X509KeyStorageFlags.EphemeralKeySet);

        return Assinador.Assinar(xml, certificado);
    }

    private static string Erros(BuildingBlocks.Error? erro) =>
        erro is null ? string.Empty : erro.Code + ": " + string.Join(" | ", erro.Details?.SelectMany(d => d.Value.Select(m => d.Key + " -> " + m)) ?? [erro.Message]);
}
