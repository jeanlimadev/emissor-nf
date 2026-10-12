using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Emissor.Modules.Emitentes.Tests;

/// <summary>
/// Certificados autoassinados no formato que a ICP-Brasil usa: CN
/// <c>NOME:DOCUMENTO</c> e, opcionalmente, o otherName do SAN com o CPF ou
/// CNPJ. Servem para os testes do inspector e para o upload nos testes de API.
/// </summary>
public static class CertificadoDeTeste
{
    public const string CpfValido = "12345678909";
    public const string CnpjValido = "11222333000181";
    public const string Senha = "senha-teste";

    public static byte[] PfxComCn(string commonName, DateTimeOffset? validoAte = null, X509Extension? san = null) =>
        Gerar(commonName, validoAte, san).Export(X509ContentType.Pfx, Senha);

    public static byte[] PfxSemChavePrivada(string commonName) =>
        Gerar(commonName, null, null).Export(X509ContentType.Cert);

    public static X509Extension SanPessoaFisica(string cpf)
    {
        // dataNascimento(8) | CPF(11) | NIS(11) | RG(15) | orgao/UF(6)
        var valor = "01011990" + cpf + new string('0', 11) + new string('0', 15) + "SSPRJ ";
        return OtherName("2.16.76.1.3.1", valor);
    }

    public static X509Extension SanPessoaJuridica(string cnpj) => OtherName("2.16.76.1.3.3", cnpj);

    private static X509Extension OtherName(string oid, string valor)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        var contextZero = new Asn1Tag(TagClass.ContextSpecific, 0);

        using (writer.PushSequence())
        using (writer.PushSequence(contextZero))
        {
            writer.WriteObjectIdentifier(oid);

            using (writer.PushSequence(contextZero))
                writer.WriteOctetString(Encoding.ASCII.GetBytes(valor));
        }

        return new X509Extension("2.5.29.17", writer.Encode(), critical: false);
    }

    private static X509Certificate2 Gerar(string commonName, DateTimeOffset? validoAte, X509Extension? san)
    {
        using var rsa = RSA.Create(2048);
        var subject = new X500DistinguishedNameBuilder();
        subject.AddCommonName(commonName);
        subject.AddOrganizationName("ICP-Brasil");

        var request = new CertificateRequest(subject.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        if (san is not null)
            request.CertificateExtensions.Add(san);

        var inicio = DateTimeOffset.UtcNow.AddDays(-1);
        var fim = validoAte ?? inicio.AddYears(1);

        return request.CreateSelfSigned(inicio, fim);
    }
}
