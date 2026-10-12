using System.Security.Cryptography;
using Emissor.Modules.Emitentes.Infrastructure.Certificados;

namespace Emissor.Modules.Emitentes.Tests;

[TestClass]
public sealed class AesGcmCertificadoProtectorTests
{
    private static AesGcmCertificadoProtector NovoProtector() =>
        new(RandomNumberGenerator.GetBytes(AesGcmCertificadoProtector.KeyBytes));

    [TestMethod]
    public void Protege_e_abre_de_volta()
    {
        var protector = NovoProtector();
        var dados = RandomNumberGenerator.GetBytes(3000);

        var protegido = protector.Proteger(dados);
        var aberto = protector.Abrir(protegido);

        CollectionAssert.AreEqual(dados, aberto);
        CollectionAssert.AreNotEqual(dados, protegido[29..]);
    }

    [TestMethod]
    public void Mesmo_dado_gera_blobs_diferentes()
    {
        var protector = NovoProtector();
        var dados = "senha"u8.ToArray();

        CollectionAssert.AreNotEqual(protector.Proteger(dados), protector.Proteger(dados));
    }

    [TestMethod]
    public void Outra_chave_nao_abre()
    {
        var protegido = NovoProtector().Proteger("senha"u8.ToArray());

        Assert.ThrowsExactly<AuthenticationTagMismatchException>(() => NovoProtector().Abrir(protegido));
    }

    [TestMethod]
    public void Blob_adulterado_nao_abre()
    {
        var protector = NovoProtector();
        var protegido = protector.Proteger("senha"u8.ToArray());
        protegido[^1] ^= 0xFF;

        Assert.ThrowsExactly<AuthenticationTagMismatchException>(() => protector.Abrir(protegido));
    }

    [TestMethod]
    public void Chave_de_configuracao_precisa_ter_32_bytes()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() => AesGcmCertificadoProtector.FromBase64(""));
        Assert.ThrowsExactly<InvalidOperationException>(() => AesGcmCertificadoProtector.FromBase64("abc"));
        Assert.ThrowsExactly<InvalidOperationException>(() => AesGcmCertificadoProtector.FromBase64(Convert.ToBase64String(new byte[16])));
        Assert.IsNotNull(AesGcmCertificadoProtector.FromBase64(Convert.ToBase64String(new byte[32])));
    }
}
