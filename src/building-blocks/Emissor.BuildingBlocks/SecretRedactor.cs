using System.Text.RegularExpressions;

namespace Emissor.BuildingBlocks;

/// <summary>
/// Tira o segredo de um texto que vai ser mostrado ou guardado: a mensagem de
/// excecao do driver traz a connection string inteira; a resposta de um
/// provedor traz o cabecalho de autorizacao. Como o texto vem de fora, mascarar
/// e a unica saida.
/// </summary>
public static partial class SecretRedactor
{
    public const string Mask = "***";

    [GeneratedRegex(@"(?i)((?:password|senha|psw|pwd)\s*=\s*)[^;\r\n]*")]
    private static partial Regex ConnectionPasswordPattern();

    [GeneratedRegex(@"(?i)([""']?(?:proxy-)?authorization[""']?\s*[:=]\s*[""']?)[^,;{}""'\r\n]+")]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex(@"(?i)\b((?:bearer|basic)\s+)[A-Za-z0-9\-._~+/=]+")]
    private static partial Regex AuthorizationSchemePattern();

    [GeneratedRegex(@"(?i)\b([a-z][a-z0-9+.\-]*://[^\s:/@]*:)[^\s/@]+@")]
    private static partial Regex UrlCredentialPattern();

    [GeneratedRegex(@"(?i)([""']?(?:password|passwd|senha|secret|segredo|credential|credencial|signature|assinatura|client[_-]?secret|api[_-]?key|apikey|x-api-key|access[_-]?key|access[_-]?token|refresh[_-]?token|private[_-]?key|token)[""']?\s*[:=]\s*)([""']?)[^""',;}&\s]+")]
    private static partial Regex ProviderSecretPattern();

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var redacted = ConnectionPasswordPattern().Replace(text, $"$1{Mask}");
        redacted = UrlCredentialPattern().Replace(redacted, $"$1{Mask}@");
        redacted = AuthorizationHeaderPattern().Replace(redacted, $"$1{Mask}");
        redacted = AuthorizationSchemePattern().Replace(redacted, $"$1{Mask}");

        return ProviderSecretPattern().Replace(redacted, $"$1$2{Mask}");
    }

    public static string Truncate(string text, int maxLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);

        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        const string ellipsis = "...";

        return maxLength <= ellipsis.Length
            ? text[..maxLength]
            : text[..(maxLength - ellipsis.Length)] + ellipsis;
    }
}
