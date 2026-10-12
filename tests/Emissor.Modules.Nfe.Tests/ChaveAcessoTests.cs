using Emissor.Modules.Nfe.Domain;

namespace Emissor.Modules.Nfe.Tests;

[TestClass]
public sealed class ChaveAcessoTests
{
    private static readonly DateTimeOffset Emissao = new(2026, 10, 11, 15, 30, 0, TimeSpan.FromHours(-3));

    [TestMethod]
    public void Monta_a_chave_com_cpf_preenchido_a_esquerda()
    {
        var chave = ChaveAcesso.Criar(33, Emissao, "12345678909", 1, 123, "87654321").Value!;

        Assert.AreEqual(44, chave.Valor.Length);
        Assert.AreEqual("33", chave.Valor[..2]);
        Assert.AreEqual("2610", chave.AnoMes);
        Assert.AreEqual("00012345678909", chave.Documento);
        Assert.AreEqual(55, chave.Modelo);
        Assert.AreEqual(1, chave.Serie);
        Assert.AreEqual(123, chave.Numero);
        Assert.AreEqual(1, chave.TipoEmissao);
        Assert.AreEqual("87654321", chave.CodigoNumerico);
        Assert.AreEqual(ChaveAcesso.CalcularDigitoVerificador(chave.Valor[..43]), chave.DigitoVerificador);
        Assert.AreEqual("NFe" + chave.Valor, chave.Id);
    }

    [TestMethod]
    public void Monta_a_chave_com_cnpj()
    {
        var chave = ChaveAcesso.Criar(35, Emissao, "11222333000181", 2, 1, "00000002").Value!;

        Assert.AreEqual("11222333000181", chave.Documento);
        Assert.AreEqual("002", chave.Valor.Substring(22, 3));
        Assert.AreEqual("000000001", chave.Valor.Substring(25, 9));
    }

    [TestMethod]
    public void Digito_verificador_segue_o_modulo_11_do_manual()
    {
        // Exemplo do Manual de Orientacao do Contribuinte (chave da NF-e de demonstracao).
        Assert.AreEqual(5, ChaveAcesso.CalcularDigitoVerificador("5206043300991100250655012000000780026730161"));
        Assert.IsTrue(ChaveAcesso.Parse("52060433009911002506550120000007800267301615").IsSuccess);
    }

    [TestMethod]
    public void Parse_recusa_dv_errado_e_tamanho_errado()
    {
        Assert.AreEqual("nfe.chave_dv_invalido", ChaveAcesso.Parse("52060433009911002506550120000007800267301616").Error!.Code);
        Assert.AreEqual("nfe.chave_invalida", ChaveAcesso.Parse("123").Error!.Code);
        Assert.IsTrue(ChaveAcesso.Parse("NFe52060433009911002506550120000007800267301615").IsSuccess, "aceita o prefixo NFe do atributo Id");
    }

    [TestMethod]
    public void Recusa_cnf_igual_ao_numero_e_documento_alfanumerico()
    {
        Assert.AreEqual("nfe.chave_cnf_igual_numero", ChaveAcesso.Criar(33, Emissao, "12345678909", 1, 123, "00000123").Error!.Code);
        // CNPJ alfanumerico e aceito (NT 2025.001); o que nao passa e letra fora das 12 primeiras posicoes ou minuscula.
        Assert.IsTrue(ChaveAcesso.Criar(33, Emissao, "12ABC345000195", 1, 1, "00000002").IsSuccess);
        Assert.AreEqual("nfe.chave_documento_invalido", ChaveAcesso.Criar(33, Emissao, "1234567800019A", 1, 1, "00000002").Error!.Code);
        Assert.AreEqual("nfe.chave_uf_invalida", ChaveAcesso.Criar(99, Emissao, "12345678909", 1, 1, "00000002").Error!.Code);
    }
}
