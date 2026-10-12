using System.Security.Cryptography.X509Certificates;
using Emissor.Modules.Emitentes.Tests;
using Emissor.Modules.Nfe.Infrastructure.Sefaz;

namespace Emissor.Modules.Nfe.Tests;

[TestClass]
public sealed class ClientesPorCertificadoTests
{
    [TestMethod]
    public void Certificado_guardado_sobrevive_ao_dispose_do_original_e_o_cliente_e_reaproveitado()
    {
        using var cache = new ClientesPorCertificado(TimeSpan.FromSeconds(5));
        var pfx = CertificadoDeTeste.PfxComCn("X:12345678909");
        string thumbprint;
        HttpClient primeiro;

        using (var original = X509CertificateLoader.LoadPkcs12(pfx, CertificadoDeTeste.Senha, X509KeyStorageFlags.EphemeralKeySet))
        {
            thumbprint = original.Thumbprint;
            primeiro = cache.Obter(original);
        }

        // O original ja foi descartado: a copia do cache ainda tem a chave privada e consegue assinar.
        var guardado = cache.CertificadoGuardado(thumbprint)!;
        Assert.IsTrue(guardado.HasPrivateKey);
        using var rsa = guardado.GetRSAPrivateKey()!;
        var assinatura = rsa.SignData(new byte[] { 1, 2, 3 }, System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        Assert.AreEqual(256, assinatura.Length);

        using var outraInstancia = X509CertificateLoader.LoadPkcs12(pfx, CertificadoDeTeste.Senha, X509KeyStorageFlags.EphemeralKeySet);
        Assert.AreSame(primeiro, cache.Obter(outraInstancia), "mesmo thumbprint, mesmo cliente (e mesma conexao TLS)");
    }
}
