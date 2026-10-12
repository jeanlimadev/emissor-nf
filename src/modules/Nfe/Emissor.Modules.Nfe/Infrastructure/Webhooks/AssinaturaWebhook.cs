using System.Security.Cryptography;
using System.Text;

namespace Emissor.Modules.Nfe.Infrastructure.Webhooks;

/// <summary>
/// HMAC-SHA256 do corpo com o segredo do webhook, no formato
/// <c>sha256=&lt;hex minusculo&gt;</c>. O sistema cliente recalcula sobre os
/// bytes exatos que recebeu e compara em tempo constante.
/// </summary>
public static class AssinaturaWebhook
{
    public const string HeaderAssinatura = "X-Emissor-Signature";
    public const string HeaderEvento = "X-Emissor-Event";
    public const string HeaderEntrega = "X-Emissor-Delivery";
    public const string HeaderTimestamp = "X-Emissor-Timestamp";
    private const string Prefixo = "sha256=";

    public static string Assinar(byte[] segredo, ReadOnlySpan<byte> corpo) =>
        Prefixo + Convert.ToHexStringLower(HMACSHA256.HashData(segredo, corpo));

    public static bool Conferir(byte[] segredo, ReadOnlySpan<byte> corpo, string? assinatura)
    {
        if (assinatura is null || !assinatura.StartsWith(Prefixo, StringComparison.Ordinal))
            return false;

        var esperada = Encoding.ASCII.GetBytes(Assinar(segredo, corpo));
        var recebida = Encoding.ASCII.GetBytes(assinatura);

        return CryptographicOperations.FixedTimeEquals(esperada, recebida);
    }

    /// <summary>32 bytes aleatorios em base64url: cabe num header e num arquivo de configuracao.</summary>
    public static string GerarSegredo() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
