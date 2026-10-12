using System.Security.Cryptography;
using System.Text;
using Emissor.BuildingBlocks;

namespace Emissor.Modules.Tenancy.Domain;

/// <summary>
/// Gera e confere chaves no formato <c>emk_&lt;prefixo8&gt;_&lt;segredo&gt;</c>.
/// O hash guardado e o SHA-256 da chave inteira: a chave ja e aleatoria e
/// longa, entao nao precisa de sal nem de hash lento como senha de pessoa.
/// </summary>
public static class ApiKeyGenerator
{
    public const string Prefix = "emk_";
    private const string Alfabeto = "abcdefghijklmnopqrstuvwxyz0123456789";

    public sealed record ChaveGerada(string Chave, string Prefixo, byte[] Hash);

    public static ChaveGerada Gerar()
    {
        var prefixo = RandomNumberGenerator.GetString(Alfabeto, ApiKey.PrefixoLength);
        var segredo = RandomNumberGenerator.GetString(Alfabeto, 40);
        var chave = $"{Prefix}{prefixo}_{segredo}";

        return new ChaveGerada(chave, prefixo, Hash(chave));
    }

    public static byte[] Hash(string chave) => SHA256.HashData(Encoding.UTF8.GetBytes(chave));

    /// <summary>O prefixo de uma chave apresentada, ou <c>null</c> quando o formato nao e o nosso.</summary>
    public static string? ExtrairPrefixo(string? chave)
    {
        if (string.IsNullOrWhiteSpace(chave) || !chave.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        var resto = chave.AsSpan(Prefix.Length);
        var separador = resto.IndexOf('_');

        if (separador != ApiKey.PrefixoLength)
            return null;

        return resto[..separador].ToString();
    }

    public static bool Confere(ApiKey apiKey, string chave) =>
        CryptographicOperations.FixedTimeEquals(apiKey.Hash, Hash(chave));
}

public static class ApiKeyFactory
{
    public static Result<(ApiKey ApiKey, string ChaveEmTexto)> Criar(Guid tenantId, string? nome, DateTimeOffset agora)
    {
        var nomeLimpo = (nome ?? string.Empty).Trim();

        if (nomeLimpo.Length is 0 or > ApiKey.NomeMaxLength)
            return Result<(ApiKey, string)>.Failure(TenancyErrors.NomeApiKeyInvalido);

        var gerada = ApiKeyGenerator.Gerar();
        var apiKey = ApiKey.Criar(tenantId, nomeLimpo, gerada.Prefixo, gerada.Hash, agora);

        return Result<(ApiKey, string)>.Success((apiKey, gerada.Chave));
    }
}
