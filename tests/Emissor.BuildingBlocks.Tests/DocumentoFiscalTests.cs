using Emissor.BuildingBlocks;

namespace Emissor.BuildingBlocks.Tests;

[TestClass]
public sealed class DocumentoFiscalTests
{
    [TestMethod]
    [DataRow("123.456.789-09")]
    [DataRow("12345678909")]
    [DataRow("529.982.247-25")]
    public void Cpf_valido_e_aceito_com_ou_sem_mascara(string cpf) =>
        Assert.IsTrue(DocumentoFiscal.IsCpfValido(cpf));

    [TestMethod]
    [DataRow("")]
    [DataRow(null)]
    [DataRow("11111111111")]
    [DataRow("12345678900")]
    [DataRow("1234567890")]
    [DataRow("123456789090")]
    public void Cpf_invalido_e_recusado(string? cpf) =>
        Assert.IsFalse(DocumentoFiscal.IsCpfValido(cpf));

    [TestMethod]
    [DataRow("11.222.333/0001-81")]
    [DataRow("11222333000181")]
    [DataRow("12.ABC.345/01DE-35")]
    public void Cnpj_valido_numerico_ou_alfanumerico_e_aceito(string cnpj) =>
        Assert.IsTrue(DocumentoFiscal.IsCnpjValido(cnpj));

    [TestMethod]
    [DataRow("")]
    [DataRow("00000000000000")]
    [DataRow("11222333000182")]
    [DataRow("1122233300018")]
    [DataRow("12.ABC.345/01DE-3A")]
    public void Cnpj_invalido_e_recusado(string cnpj) =>
        Assert.IsFalse(DocumentoFiscal.IsCnpjValido(cnpj));

    [TestMethod]
    public void Cnpj_alfanumerico_minusculo_e_normalizado_para_maiusculo() =>
        Assert.AreEqual("12ABC34501DE35", DocumentoFiscal.SomenteCaracteresValidos("12.abc.345/01de-35"));
}
