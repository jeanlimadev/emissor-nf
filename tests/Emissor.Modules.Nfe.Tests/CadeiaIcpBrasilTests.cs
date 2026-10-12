using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using Emissor.Modules.Emitentes.Tests;
using Emissor.Modules.Nfe.Infrastructure.Sefaz;

namespace Emissor.Modules.Nfe.Tests;

[TestClass]
public sealed class CadeiaIcpBrasilTests
{
    [TestMethod]
    public void Raizes_embutidas_sao_da_icp_brasil_e_estao_na_validade()
    {
        var raizes = CadeiaIcpBrasil.RaizesConfiaveis;

        Assert.AreEqual(5, raizes.Count, "v5, v10, v11, v12 e v13");

        foreach (var raiz in raizes)
        {
            StringAssert.Contains(raiz.Subject, "Autoridade Certificadora Raiz Brasileira");
            StringAssert.Contains(raiz.Subject, "O=ICP-Brasil");
            Assert.IsTrue(raiz.NotAfter > DateTime.UtcNow.AddYears(1), $"{raiz.Subject} vence em {raiz.NotAfter:d}");
            Assert.AreEqual(raiz.Subject, raiz.Issuer, "raiz e autoassinada");
        }
    }

    [TestMethod]
    public void Sem_erro_de_politica_aceita_e_erro_de_nome_recusa()
    {
        using var qualquer = X509CertificateLoader.LoadPkcs12(CertificadoDeTeste.PfxComCn("X:12345678909"), CertificadoDeTeste.Senha);

        Assert.IsTrue(CadeiaIcpBrasil.ValidarServidor(this, qualquer, null, SslPolicyErrors.None));
        Assert.IsFalse(CadeiaIcpBrasil.ValidarServidor(this, qualquer, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        Assert.IsFalse(CadeiaIcpBrasil.ValidarServidor(this, null, null, SslPolicyErrors.RemoteCertificateNotAvailable));
    }

    [TestMethod]
    public void Certificado_fora_da_icp_brasil_com_erro_de_cadeia_e_recusado()
    {
        using var autoassinado = X509CertificateLoader.LoadPkcs12(CertificadoDeTeste.PfxComCn("X:12345678909"), CertificadoDeTeste.Senha);

        Assert.IsFalse(CadeiaIcpBrasil.ValidarServidor(this, autoassinado, null, SslPolicyErrors.RemoteCertificateChainErrors));
    }
}
