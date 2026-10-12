using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Nfe.Domain;

namespace Emissor.Modules.Nfe.Tests;

[TestClass]
public sealed class NotaFiscalTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly ChaveAcesso Chave = ChaveAcesso.Criar(33, Agora, "12345678909", 1, 7, "87654321").Value!;

    private static NotaFiscal Nova() =>
        NotaFiscal.Receber(Guid.NewGuid(), Guid.NewGuid(), AmbienteSefaz.Homologacao, 1, "pedido-1", "hash", "{}", Agora);

    [TestMethod]
    public void Nasce_recebida_e_na_fila()
    {
        var nota = Nova();

        Assert.AreEqual(NfeStatus.Recebida, nota.Status);
        Assert.IsTrue(nota.EstaNaFila);
        Assert.IsFalse(nota.EhFinal);
        Assert.AreEqual(Agora, nota.ProximaTentativaEm);
        Assert.IsNull(nota.Numero);
    }

    [TestMethod]
    public void Caminho_feliz_recebida_processando_autorizada()
    {
        var nota = Nova();

        Assert.IsTrue(nota.IniciarProcessamento(Agora).IsSuccess);
        Assert.AreEqual(1, nota.Tentativas);
        nota.DefinirNumeroEChave(7, Chave, Agora, Agora);
        nota.DefinirXmlAssinado("<NFe/>", Agora);
        nota.RegistrarAutorizacao(new ProtocoloSefaz(100, "Autorizado o uso da NF-e", "333260000000001", Agora, "<protNFe/>"), "<nfeProc/>", Agora);

        Assert.AreEqual(NfeStatus.Autorizada, nota.Status);
        Assert.IsTrue(nota.EhFinal);
        Assert.AreEqual("333260000000001", nota.Protocolo);
        Assert.AreEqual(100, nota.CStat);
        Assert.AreEqual("<nfeProc/>", nota.XmlProcNfe);
        Assert.IsNull(nota.ProximaTentativaEm);
    }

    [TestMethod]
    public void Nota_final_nao_volta_para_processamento()
    {
        var nota = Nova();
        nota.IniciarProcessamento(Agora);
        nota.RegistrarRejeicao(539, "Duplicidade", Agora);

        var resultado = nota.IniciarProcessamento(Agora);

        Assert.IsTrue(resultado.IsFailure);
        Assert.AreEqual("nfe.transicao_invalida", resultado.Error!.Code);
    }

    [TestMethod]
    public void Pendente_de_consulta_volta_para_a_fila_com_espera()
    {
        var nota = Nova();
        nota.IniciarProcessamento(Agora);
        nota.DefinirNumeroEChave(7, Chave, Agora, Agora);

        nota.MarcarPendenteConsulta("timeout", "123456789012345", TimeSpan.FromSeconds(30), Agora);

        Assert.AreEqual(NfeStatus.PendenteConsulta, nota.Status);
        Assert.IsTrue(nota.EstaNaFila);
        Assert.AreEqual(Agora.AddSeconds(30), nota.ProximaTentativaEm);
        Assert.AreEqual("123456789012345", nota.Recibo);
        Assert.AreEqual(7, nota.Numero, "o numero reservado fica");
    }

    [TestMethod]
    public void Reenvio_so_de_rejeitada_ou_falha_e_mantem_numero_mas_refaz_chave()
    {
        var nota = Nova();
        nota.IniciarProcessamento(Agora);
        nota.DefinirNumeroEChave(7, Chave, Agora, Agora);
        nota.DefinirXmlAssinado("<NFe/>", Agora);

        Assert.AreEqual("nfe.reenvio_nao_permitido", nota.Reenviar(null, null, Agora).Error!.Code, "em processamento nao reenvia");

        nota.RegistrarRejeicao(778, "NCM inexistente", Agora);
        var reenvio = nota.Reenviar("{\"corrigido\":true}", "hash2", Agora);

        Assert.IsTrue(reenvio.IsSuccess);
        Assert.AreEqual(NfeStatus.Recebida, nota.Status);
        Assert.AreEqual(7, nota.Numero);
        Assert.IsNull(nota.ChaveAcesso);
        Assert.IsNull(nota.XmlAssinado);
        Assert.IsNull(nota.CStat);
        Assert.AreEqual(0, nota.Tentativas);
        Assert.AreEqual("hash2", nota.PedidoHash);
    }

    [TestMethod]
    public void Pendente_de_consulta_reenviada_sem_corpo_so_antecipa_a_tentativa()
    {
        var nota = Nova();
        nota.IniciarProcessamento(Agora);
        nota.DefinirNumeroEChave(7, Chave, Agora, Agora);
        nota.DefinirXmlAssinado("<NFe/>", Agora);
        nota.MarcarPendenteConsulta("HTTP 403", null, TimeSpan.FromMinutes(30), Agora);

        var depois = Agora.AddMinutes(5);
        Assert.AreEqual("nfe.reenvio_pendente_com_pedido", nota.Reenviar("{}", "h", depois).Error!.Code, "com pedido novo nao pode: a nota pode ja constar");
        Assert.IsTrue(nota.Reenviar(null, null, depois).IsSuccess);

        Assert.AreEqual(NfeStatus.PendenteConsulta, nota.Status);
        Assert.AreEqual(depois, nota.ProximaTentativaEm);
        Assert.AreEqual(Chave.Valor, nota.ChaveAcesso, "chave e XML ficam: a consulta precisa deles");
        Assert.AreEqual("<NFe/>", nota.XmlAssinado);
        Assert.AreEqual(1, nota.Tentativas);
    }

    [TestMethod]
    public void Denegacao_e_final_e_guarda_protocolo()
    {
        var nota = Nova();
        nota.IniciarProcessamento(Agora);
        nota.RegistrarDenegacao(new ProtocoloSefaz(302, "Irregularidade fiscal do destinatário", "333260000000002", Agora, "<protNFe/>"), "<nfeProc/>", Agora);

        Assert.AreEqual(NfeStatus.Denegada, nota.Status);
        Assert.IsTrue(nota.EhFinal);
        Assert.AreEqual(302, nota.CStat);
        Assert.IsTrue(nota.Reenviar(null, null, Agora).IsFailure, "denegada consome o numero: nao reenvia");
    }

    [TestMethod]
    public void Codigos_da_sefaz_classificados_como_o_manual_descreve()
    {
        Assert.IsTrue(CodigosSefaz.EhDenegacao(110));
        Assert.IsTrue(CodigosSefaz.EhDenegacao(301));
        Assert.IsTrue(CodigosSefaz.EhDenegacao(302));
        Assert.IsTrue(CodigosSefaz.EhDenegacao(303));
        Assert.IsFalse(CodigosSefaz.EhDenegacao(100));
        Assert.IsTrue(CodigosSefaz.EhTemporario(103));
        Assert.IsTrue(CodigosSefaz.EhTemporario(105));
        Assert.IsTrue(CodigosSefaz.EhTemporario(108));
        Assert.IsTrue(CodigosSefaz.EhTemporario(109));
        Assert.IsFalse(CodigosSefaz.EhTemporario(539));
    }
}
