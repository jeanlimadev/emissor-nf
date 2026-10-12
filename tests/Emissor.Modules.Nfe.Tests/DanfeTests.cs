using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Emissor.Modules.Emitentes.Tests;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using Emissor.Modules.Nfe.Infrastructure.Assinatura;
using Emissor.Modules.Nfe.Infrastructure.Danfe;
using Emissor.Modules.Nfe.Infrastructure.Sefaz;
using Emissor.Modules.Nfe.Infrastructure.Xml;

namespace Emissor.Modules.Nfe.Tests;

[TestClass]
public sealed class DanfeTests
{
    private const string Chave = "33261000012345678909550010000000011080676244";

    [TestMethod]
    public void Code128_tabela_consistente_e_checksum_correto()
    {
        Assert.IsTrue(Code128.TabelaConsistente(), "cada simbolo tem 11 modulos e o stop 13");

        // Exemplo classico: "123456" em Code C = Start C(105), 12, 34, 56, checksum, Stop.
        // checksum = (105 + 12*1 + 34*2 + 56*3) mod 103 = (105 + 12 + 68 + 168) mod 103 = 353 mod 103 = 44
        var simbolos = Code128.Simbolos("123456");
        CollectionAssert.AreEqual(new[] { 105, 12, 34, 56, 44, 106 }, simbolos.ToArray());

        var larguras = Code128.Larguras(Chave);
        Assert.AreEqual(44 / 2 + 3, Code128.Simbolos(Chave).Count, "22 pares + start + checksum + stop");
        Assert.AreEqual((44 / 2 + 2) * 11 + 13, larguras.Sum(), "largura total em modulos");

        var svg = Code128.Svg(Chave);
        StringAssert.StartsWith(svg, "<svg");
        Assert.AreEqual(larguras.Count / 2 + 1, Regex.Matches(svg, "fill=\"#000\"").Count, "uma barra preta por elemento impar");
    }

    [TestMethod]
    public void Code128_recusa_quantidade_impar_de_digitos()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Code128.Simbolos("123"));
        Assert.ThrowsExactly<ArgumentException>(() => Code128.Simbolos("12A4"));
    }

    [TestMethod]
    public void Leitor_extrai_os_dados_da_danfe_do_procnfe()
    {
        var proc = ProcNfeAutorizado(PedidosDeTeste.EmitentePf(), PedidosDeTeste.PedidoRegimeNormal());

        var dados = DanfeLeitor.Ler(proc);

        Assert.AreEqual(44, dados.ChaveAcesso.Length);
        Assert.IsTrue(dados.Homologacao);
        Assert.AreEqual(1, dados.TipoOperacao);
        Assert.AreEqual(1, dados.Serie);
        Assert.AreEqual(123, dados.Numero);
        Assert.AreEqual("Venda de producao do estabelecimento", dados.NaturezaOperacao);
        Assert.AreEqual("123.456.789-09", dados.Emitente.Documento);
        Assert.AreEqual("12345678", dados.Emitente.InscricaoEstadual);
        Assert.AreEqual("Estrada do Produtor, KM 12", dados.Emitente.Endereco);
        Assert.AreEqual("(21) 2222-3333", dados.Emitente.Telefone);
        Assert.AreEqual("11.222.333/0001-81", dados.Destinatario.Documento);
        Assert.AreEqual(DfeNetNfeXmlBuilder.NomeDestinatarioHomologacao, dados.Destinatario.Nome);
        Assert.AreEqual("333260000000001", dados.Protocolo!.Numero);
        Assert.AreEqual(100, dados.Protocolo.CStat);
        Assert.AreEqual(250m, dados.Totais.Nota);
        Assert.AreEqual(45m, dados.Totais.Icms);
        Assert.AreEqual(250m, dados.Totais.BaseIbsCbs);
        Assert.AreEqual(2.25m, dados.Totais.Cbs);
        Assert.AreEqual(1, dados.Transporte.ModalidadeFrete);
        Assert.AreEqual(10, dados.Transporte.Volumes.Single().Quantidade);
        Assert.AreEqual(1, dados.Itens.Count);
        Assert.AreEqual("000", dados.Itens[0].SituacaoTributaria, "origem 0 + CST 00");
        Assert.AreEqual(5101, dados.Itens[0].Cfop);
        Assert.AreEqual(18m, dados.Itens[0].AliquotaIcms);
        Assert.AreEqual("Pedido de teste do emissor-nf.", dados.InformacoesComplementares);
    }

    [TestMethod]
    public void Leitor_le_nfe_sem_protocolo_e_simples_nacional()
    {
        var xml = XmlAssinado(PedidosDeTeste.EmitentePj(), PedidosDeTeste.PedidoSimples());

        var dados = DanfeLeitor.Ler(xml);

        Assert.IsNull(dados.Protocolo);
        Assert.AreEqual("11.222.333/0001-81", dados.Emitente.Documento);
        Assert.AreEqual("0102", dados.Itens[0].SituacaoTributaria, "origem 0 + CSOSN 102");
        Assert.AreEqual(9, dados.Transporte.ModalidadeFrete);
        Assert.AreEqual(0, dados.Transporte.Volumes.Count);
        Assert.IsNull(dados.Totais.BaseIbsCbs);
    }

    [TestMethod]
    public void Renderiza_pdf_de_uma_pagina_para_nota_autorizada()
    {
        var proc = ProcNfeAutorizado(PedidosDeTeste.EmitentePf(), PedidosDeTeste.PedidoRegimeNormal());

        var pdf = new QuestPdfDanfeRenderer().Renderizar(proc, null);
        SalvarSePedido(pdf, "danfe-autorizada.pdf");

        Assert.IsTrue(pdf.Length > 5_000, $"pdf com {pdf.Length} bytes");
        Assert.AreEqual("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.AreEqual(1, Paginas(pdf));
    }

    [TestMethod]
    public void Nota_com_muitos_itens_pagina_e_repete_o_cabecalho()
    {
        var pedido = PedidosDeTeste.PedidoRegimeNormal();
        var itens = Enumerable.Range(0, 60).Select(i =>
        {
            var item = PedidosDeTeste.ItemRegimeNormal(quantidade: 1m, valorUnitario: 10m);
            return item with { Produto = item.Produto with { Codigo = $"ITEM-{i:000}", Descricao = $"Produto de teste numero {i} com descricao longa para ocupar a linha" } };
        }).ToList();
        var total = itens.Count * 10m;
        pedido = pedido with
        {
            Itens = itens,
            Pagamento = new Contracts.Nfe.PagamentoDto([new Contracts.Nfe.DetalhePagamentoDto(null, Contracts.Nfe.FormaPagamento.Dinheiro, total, null)], null)
        };

        var pdf = new QuestPdfDanfeRenderer().Renderizar(ProcNfeAutorizado(PedidosDeTeste.EmitentePf(), pedido), null);
        SalvarSePedido(pdf, "danfe-muitos-itens.pdf");

        Assert.IsTrue(Paginas(pdf) >= 2, $"esperava mais de uma pagina, veio {Paginas(pdf)}");
    }

    [TestMethod]
    public void Previa_sem_protocolo_tambem_renderiza()
    {
        var xml = XmlAssinado(PedidosDeTeste.EmitentePj(), PedidosDeTeste.PedidoSimples());

        var pdf = new QuestPdfDanfeRenderer().Renderizar(xml, "PRÉVIA - SEM VALOR FISCAL");

        Assert.AreEqual("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
    }

    // ----- apoio ---------------------------------------------------------------------------------

    /// <summary>Com a variavel DANFE_SAIDA apontando para uma pasta, grava os PDFs para inspecao visual.</summary>
    private static void SalvarSePedido(byte[] pdf, string nome)
    {
        var pasta = Environment.GetEnvironmentVariable("DANFE_SAIDA");

        if (!string.IsNullOrEmpty(pasta))
            File.WriteAllBytes(Path.Combine(pasta, nome), pdf);
    }

    private static int Paginas(byte[] pdf) =>
        Regex.Matches(Encoding.Latin1.GetString(pdf), @"/Type\s*/Page[^s]").Count;

    private static string XmlAssinado(Emitentes.Domain.Emitente emitente, Contracts.Nfe.EmissaoNfeRequest pedido)
    {
        var dataEmissao = Uf.NoFuso(pedido.Identificacao.DataEmissao!.Value, emitente.Endereco.Uf);
        var chave = ChaveAcesso.Criar(Uf.Codigo(emitente.Endereco.Uf), dataEmissao, emitente.Documento, pedido.Identificacao.Serie, 123, "87654321").Value!;
        var xml = new DfeNetNfeXmlBuilder().Montar(new NfeMontagem(emitente, chave, dataEmissao, pedido));
        Assert.IsTrue(xml.IsSuccess, xml.Error?.Message);

        using var certificado = X509CertificateLoader.LoadPkcs12(CertificadoDeTeste.PfxComCn($"X:{emitente.Documento}"), CertificadoDeTeste.Senha, X509KeyStorageFlags.EphemeralKeySet);
        return new XmlDsigNfeAssinador().Assinar(xml.Value!, certificado);
    }

    private static string ProcNfeAutorizado(Emitentes.Domain.Emitente emitente, Contracts.Nfe.EmissaoNfeRequest pedido)
    {
        var nfe = XmlAssinado(emitente, pedido);
        var chave = Regex.Match(nfe, "Id=\"NFe([0-9]{44})\"").Groups[1].Value;
        var prot = $"""<protNFe xmlns="http://www.portalfiscal.inf.br/nfe" versao="4.00"><infProt Id="ID333260000000001"><tpAmb>2</tpAmb><verAplic>SVRS</verAplic><chNFe>{chave}</chNFe><dhRecbto>2026-10-11T12:31:00-03:00</dhRecbto><nProt>333260000000001</nProt><digVal>abc=</digVal><cStat>100</cStat><xMotivo>Autorizado o uso da NF-e</xMotivo></infProt></protNFe>""";

        return MensagensSefaz.ProcNFe(nfe, prot);
    }
}
