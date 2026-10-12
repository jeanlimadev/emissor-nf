using System.Runtime.InteropServices;

namespace Emissor.BuildingBlocks;

/// <summary>
/// A assinatura da NF-e e RSA-SHA1 por exigencia da SEFAZ. Fedora e RHEL
/// (politica de criptografia DEFAULT) proibem SHA-1 em assinaturas no OpenSSL
/// do sistema, e o .NET falha com "invalid digest". Este shim detecta essa
/// politica e aponta <c>OPENSSL_CONF</c> para uma configuracao que libera
/// SHA-1 so para este processo, sem mexer na maquina. Precisa rodar antes do
/// primeiro uso de criptografia, porque o OpenSSL le a configuracao uma vez.
/// Em Debian (imagem do container), macOS e Windows nao faz nada.
/// </summary>
public static class OpenSslSha1
{
    public const string VariavelOpenSsl = "OPENSSL_CONF";
    private const string ArquivoPolitica = "/etc/crypto-policies/back-ends/opensslcnf.config";
    private const string ChavePolitica = "rh-allow-sha1-signatures = no";

    private const string Configuracao = """
        # Gerado pelo emissor-nf: libera assinaturas SHA-1 (exigidas pela NF-e) neste processo.
        openssl_conf = openssl_init

        [openssl_init]
        alg_section = evp_properties

        [evp_properties]
        rh-allow-sha1-signatures = yes

        """;

    /// <summary>Devolve true quando aplicou a configuracao; false quando nao era necessario ou nao foi possivel.</summary>
    public static bool PermitirSeNecessario()
    {
        if (!OperatingSystem.IsLinux())
            return false;

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(VariavelOpenSsl)))
            return false;

        try
        {
            if (!File.Exists(ArquivoPolitica) || !File.ReadAllText(ArquivoPolitica).Contains(ChavePolitica, StringComparison.Ordinal))
                return false;

            var caminho = Path.Combine(Path.GetTempPath(), "emissor-nf-openssl-sha1.cnf");
            File.WriteAllText(caminho, Configuracao);

            // Environment.SetEnvironmentVariable so muda a copia gerenciada no Linux; o OpenSSL le o ambiente nativo.
            return setenv(VariavelOpenSsl, caminho, 1) == 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

#pragma warning disable SYSLIB1054 // LibraryImport exigiria unsafe; DllImport basta para uma chamada no arranque.
    [DllImport("libc", SetLastError = true)]
    private static extern int setenv(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
        int overwrite);
#pragma warning restore SYSLIB1054
}
