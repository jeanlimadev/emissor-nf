using Emissor.Modules.Emitentes.Domain;
using Emissor.Modules.Emitentes.Infrastructure.Certificados;

namespace Emissor.Modules.Emitentes.Tests;

[TestClass]
public sealed class X509CertificadoInspectorTests
{
    private readonly X509CertificadoInspector _inspector = new();

    [TestMethod]
    public void Le_cpf_e_titular_do_cn()
    {
        var pfx = CertificadoDeTeste.PfxComCn($"PRODUTOR TESTE:{CertificadoDeTeste.CpfValido}");

        var resultado = _inspector.Inspecionar(pfx, CertificadoDeTeste.Senha);

        Assert.IsTrue(resultado.IsSuccess, resultado.Error?.Message);
        Assert.AreEqual(CertificadoDeTeste.CpfValido, resultado.Value!.Documento);
        Assert.AreEqual("PRODUTOR TESTE", resultado.Value.Titular);
        Assert.IsTrue(resultado.Value.ValidoAte > DateTimeOffset.UtcNow);
    }

    [TestMethod]
    public void Le_cnpj_do_cn_com_mascara()
    {
        var pfx = CertificadoDeTeste.PfxComCn("EMPRESA LTDA:11.222.333/0001-81");

        var resultado = _inspector.Inspecionar(pfx, CertificadoDeTeste.Senha);

        Assert.IsTrue(resultado.IsSuccess, resultado.Error?.Message);
        Assert.AreEqual(CertificadoDeTeste.CnpjValido, resultado.Value!.Documento);
    }

    [TestMethod]
    public void Prefere_o_san_icp_brasil_ao_cn()
    {
        var pfx = CertificadoDeTeste.PfxComCn("NOME SEM DOCUMENTO", san: CertificadoDeTeste.SanPessoaFisica(CertificadoDeTeste.CpfValido));

        var resultado = _inspector.Inspecionar(pfx, CertificadoDeTeste.Senha);

        Assert.IsTrue(resultado.IsSuccess, resultado.Error?.Message);
        Assert.AreEqual(CertificadoDeTeste.CpfValido, resultado.Value!.Documento);
        Assert.AreEqual("NOME SEM DOCUMENTO", resultado.Value.Titular);
    }

    [TestMethod]
    public void Le_cnpj_do_san_icp_brasil()
    {
        var pfx = CertificadoDeTeste.PfxComCn("EMPRESA", san: CertificadoDeTeste.SanPessoaJuridica(CertificadoDeTeste.CnpjValido));

        var resultado = _inspector.Inspecionar(pfx, CertificadoDeTeste.Senha);

        Assert.IsTrue(resultado.IsSuccess, resultado.Error?.Message);
        Assert.AreEqual(CertificadoDeTeste.CnpjValido, resultado.Value!.Documento);
    }

    [TestMethod]
    public void Senha_errada_e_ilegivel()
    {
        var pfx = CertificadoDeTeste.PfxComCn($"X:{CertificadoDeTeste.CpfValido}");

        var resultado = _inspector.Inspecionar(pfx, "outra");

        Assert.AreEqual(EmitenteErrors.CertificadoIlegivel, resultado.Error);
    }

    [TestMethod]
    public void Sem_documento_e_recusado()
    {
        var pfx = CertificadoDeTeste.PfxComCn("SO UM NOME");

        var resultado = _inspector.Inspecionar(pfx, CertificadoDeTeste.Senha);

        Assert.AreEqual(EmitenteErrors.CertificadoSemDocumento, resultado.Error);
    }

    [TestMethod]
    public void Sem_chave_privada_e_recusado()
    {
        var cer = CertificadoDeTeste.PfxSemChavePrivada($"X:{CertificadoDeTeste.CpfValido}");

        var resultado = _inspector.Inspecionar(cer, string.Empty);

        // Um .cer nao e PKCS#12: o loader falha antes de olhar a chave.
        Assert.IsTrue(resultado.IsFailure);
    }
}
