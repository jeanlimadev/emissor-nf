using Emissor.Contracts.Emitentes;
using Emissor.Modules.Emitentes.Domain;

namespace Emissor.Modules.Emitentes.Tests;

[TestClass]
public sealed class EmitenteTests
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    internal static Endereco EnderecoValido() => Endereco.Criar(
        "Estrada do Produtor", "KM 12", null, "Zona Rural", "3304557", "Rio de Janeiro", "rj", "20000-000", "(21) 99999-0000").Value!;

    internal static Emitente.Dados DadosPf(string? documento = CertificadoDeTeste.CpfValido) => new(
        TipoEmitente.PessoaFisica, documento, "12.345.678", "Produtor Rural Teste", null,
        RegimeTributario.RegimeNormal, AmbienteSefaz.Homologacao, EnderecoValido(), "produtor@teste.com");

    internal static Emitente.Dados DadosPj() => new(
        TipoEmitente.PessoaJuridica, "11.222.333/0001-81", "12345678", "Empresa Teste Ltda", "Empresa Teste",
        RegimeTributario.SimplesNacional, AmbienteSefaz.Homologacao, EnderecoValido(), null);

    [TestMethod]
    public void Produtor_rural_pf_e_criado_com_cpf_limpo_e_ie_so_digitos()
    {
        var emitente = Emitente.Criar(Guid.NewGuid(), DadosPf("123.456.789-09"), Agora);

        Assert.IsTrue(emitente.IsSuccess, emitente.Error?.Message);
        Assert.AreEqual("12345678909", emitente.Value!.Documento);
        Assert.AreEqual("12345678", emitente.Value.InscricaoEstadual);
        Assert.AreEqual("RJ", emitente.Value.Endereco.Uf);
        Assert.AreEqual("20000000", emitente.Value.Endereco.Cep);
        Assert.AreEqual("21999990000", emitente.Value.Endereco.Telefone);
        Assert.IsTrue(emitente.Value.Ativo);
    }

    [TestMethod]
    public void Pj_e_criada_com_cnpj_limpo()
    {
        var emitente = Emitente.Criar(Guid.NewGuid(), DadosPj(), Agora);

        Assert.IsTrue(emitente.IsSuccess, emitente.Error?.Message);
        Assert.AreEqual("11222333000181", emitente.Value!.Documento);
    }

    [TestMethod]
    public void Cpf_invalido_em_pf_e_recusado()
    {
        var emitente = Emitente.Criar(Guid.NewGuid(), DadosPf("111.111.111-11"), Agora);

        Assert.AreEqual(EmitenteErrors.CpfInvalido, emitente.Error);
    }

    [TestMethod]
    public void Cnpj_no_lugar_de_cpf_e_recusado()
    {
        var emitente = Emitente.Criar(Guid.NewGuid(), DadosPf("11222333000181"), Agora);

        Assert.AreEqual(EmitenteErrors.CpfInvalido, emitente.Error);
    }

    [TestMethod]
    public void Ie_e_obrigatoria()
    {
        var dados = DadosPf() with { InscricaoEstadual = "ISENTO" };

        var emitente = Emitente.Criar(Guid.NewGuid(), dados, Agora);

        Assert.AreEqual(EmitenteErrors.InscricaoEstadualInvalida, emitente.Error);
    }

    [TestMethod]
    public void Certificado_de_outro_titular_e_recusado()
    {
        var emitente = Emitente.Criar(Guid.NewGuid(), DadosPf(), Agora).Value!;
        var certificado = Certificado.Criar(
            emitente.TenantId, emitente.Id,
            new CertificadoInspecionado("OUTRA", CertificadoDeTeste.CnpjValido, "AB", Agora.AddDays(-1), Agora.AddYears(1)),
            [1], [2], Agora);

        var definido = emitente.DefinirCertificado(certificado, Agora);

        Assert.AreEqual(EmitenteErrors.CertificadoDeOutroTitular, definido.Error);
        Assert.IsNull(emitente.Certificado);
    }

    [TestMethod]
    public void Certificado_vencido_e_recusado()
    {
        var emitente = Emitente.Criar(Guid.NewGuid(), DadosPf(), Agora).Value!;
        var certificado = Certificado.Criar(
            emitente.TenantId, emitente.Id,
            new CertificadoInspecionado("EU", CertificadoDeTeste.CpfValido, "AB", Agora.AddYears(-2), Agora.AddDays(-1)),
            [1], [2], Agora);

        Assert.AreEqual(EmitenteErrors.CertificadoVencido, emitente.DefinirCertificado(certificado, Agora).Error);
    }

    [TestMethod]
    public void Com_certificado_nao_se_troca_o_documento()
    {
        var emitente = Emitente.Criar(Guid.NewGuid(), DadosPf(), Agora).Value!;
        var certificado = Certificado.Criar(
            emitente.TenantId, emitente.Id,
            new CertificadoInspecionado("EU", CertificadoDeTeste.CpfValido, "AB", Agora.AddDays(-1), Agora.AddYears(1)),
            [1], [2], Agora);
        Assert.IsTrue(emitente.DefinirCertificado(certificado, Agora).IsSuccess);

        var atualizado = emitente.Atualizar(DadosPf("529.982.247-25"), Agora);

        Assert.AreEqual(EmitenteErrors.DocumentoComCertificado, atualizado.Error);
    }

    [TestMethod]
    [DataRow("", "endereco.logradouro_invalido")]
    [DataRow("XX", "endereco.uf_invalida")]
    public void Endereco_invalido_aponta_o_campo(string valor, string codigoEsperado)
    {
        var endereco = codigoEsperado.Contains("uf")
            ? Endereco.Criar("Rua", "1", null, "Centro", "3304557", "Rio", valor, "20000000", null)
            : Endereco.Criar(valor, "1", null, "Centro", "3304557", "Rio", "RJ", "20000000", null);

        Assert.AreEqual(codigoEsperado, endereco.Error?.Code);
    }
}
