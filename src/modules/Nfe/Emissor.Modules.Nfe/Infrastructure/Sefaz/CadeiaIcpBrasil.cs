using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Emissor.Modules.Nfe.Infrastructure.Sefaz;

/// <summary>
/// Confianca no certificado TLS da SEFAZ. Os webservices (SVRS inclusive)
/// apresentam certificados emitidos dentro da ICP-Brasil, cuja raiz nao vem
/// nas distribuicoes Linux nem na imagem do container: sem isto a conexao
/// cai com "PartialChain". As raizes oficiais (AC Raiz Brasileira v5 a v13)
/// estao embutidas no assembly, baixadas do ITI. A validacao e a padrao do
/// sistema primeiro; so quando ela falha por cadeia e que se tenta a cadeia
/// da ICP-Brasil. Nome do host, validade e revogacao continuam exigidos.
/// </summary>
public static class CadeiaIcpBrasil
{
    private static readonly Lazy<X509Certificate2Collection> Raizes = new(CarregarRaizes, LazyThreadSafetyMode.ExecutionAndPublication);

    public static X509Certificate2Collection RaizesConfiaveis => Raizes.Value;

    /// <summary>Callback para <see cref="SslClientAuthenticationOptions.RemoteCertificateValidationCallback"/>.</summary>
    public static bool ValidarServidor(object remetente, X509Certificate? certificado, X509Chain? cadeia, SslPolicyErrors erros)
    {
        if (erros == SslPolicyErrors.None)
            return true;

        // Nome errado ou certificado ausente nao se perdoa: so o problema de cadeia e que a ICP-Brasil explica.
        if (erros != SslPolicyErrors.RemoteCertificateChainErrors || certificado is null)
            return false;

        using var servidor = new X509Certificate2(certificado);
        using var cadeiaIcp = new X509Chain();
        cadeiaIcp.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        cadeiaIcp.ChainPolicy.CustomTrustStore.AddRange(RaizesConfiaveis);
        cadeiaIcp.ChainPolicy.RevocationMode = X509RevocationMode.Online;
        cadeiaIcp.ChainPolicy.RevocationFlag = X509RevocationFlag.ExcludeRoot;
        // Falha ao baixar a lista de revogacao nao derruba a emissao; revogacao de fato derruba.
        cadeiaIcp.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreCertificateAuthorityRevocationUnknown | X509VerificationFlags.IgnoreEndRevocationUnknown;

        // As intermediarias que o servidor mandou (ex.: AC do SERPRO SSLv1) ajudam a montar a cadeia.
        if (cadeia is not null)
            foreach (var elemento in cadeia.ChainElements)
                if (!elemento.Certificate.SubjectName.RawData.AsSpan().SequenceEqual(elemento.Certificate.IssuerName.RawData))
                    cadeiaIcp.ChainPolicy.ExtraStore.Add(elemento.Certificate);

        return cadeiaIcp.Build(servidor);
    }

    private static X509Certificate2Collection CarregarRaizes()
    {
        var assembly = typeof(CadeiaIcpBrasil).Assembly;
        var colecao = new X509Certificate2Collection();

        foreach (var recurso in assembly.GetManifestResourceNames().Where(n => n.StartsWith("CadeiaIcpBrasil/", StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(recurso)!;
            using var memoria = new MemoryStream();
            stream.CopyTo(memoria);
            colecao.Add(X509CertificateLoader.LoadCertificate(memoria.ToArray()));
        }

        if (colecao.Count == 0)
            throw new InvalidOperationException("Nenhuma raiz da ICP-Brasil embutida no assembly.");

        return colecao;
    }
}
