using System.Security.Cryptography;
using Emissor.Modules.Emitentes.Application.Ports;

namespace Emissor.Modules.Emitentes.Infrastructure.Certificados;

/// <summary>
/// AES-256-GCM com a chave mestra do servico. Formato do blob:
/// <c>versao(1) | nonce(12) | tag(16) | cifrado</c>. A versao existe para
/// trocar o algoritmo ou a chave no futuro sem perder o que ja esta guardado.
/// </summary>
public sealed class AesGcmCertificadoProtector : ICertificadoProtector
{
    public const int KeyBytes = 32;
    private const byte Versao = 1;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;

    private readonly byte[] _chave;

    public AesGcmCertificadoProtector(byte[] chaveMestra)
    {
        if (chaveMestra.Length != KeyBytes)
            throw new ArgumentException($"A chave mestra deve ter {KeyBytes} bytes.", nameof(chaveMestra));

        _chave = chaveMestra;
    }

    /// <summary>Monta a partir do valor de configuracao (base64 de 32 bytes).</summary>
    public static AesGcmCertificadoProtector FromBase64(string? chaveBase64)
    {
        if (string.IsNullOrWhiteSpace(chaveBase64))
            throw new InvalidOperationException(
                "Chave mestra dos certificados ausente: configure Certificados:ChaveMestra com 32 bytes em base64 (ex.: openssl rand -base64 32).");

        byte[] chave;
        try
        {
            chave = Convert.FromBase64String(chaveBase64);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("Certificados:ChaveMestra não é base64 válido.", exception);
        }

        if (chave.Length != KeyBytes)
            throw new InvalidOperationException($"Certificados:ChaveMestra deve ter {KeyBytes} bytes; tem {chave.Length}.");

        return new AesGcmCertificadoProtector(chave);
    }

    public byte[] Proteger(ReadOnlySpan<byte> dados)
    {
        var saida = new byte[1 + NonceBytes + TagBytes + dados.Length];
        saida[0] = Versao;

        var nonce = saida.AsSpan(1, NonceBytes);
        var tag = saida.AsSpan(1 + NonceBytes, TagBytes);
        var cifrado = saida.AsSpan(1 + NonceBytes + TagBytes);

        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(_chave, TagBytes);
        aes.Encrypt(nonce, dados, cifrado, tag);

        return saida;
    }

    public byte[] Abrir(ReadOnlySpan<byte> protegido)
    {
        if (protegido.Length < 1 + NonceBytes + TagBytes || protegido[0] != Versao)
            throw new CryptographicException("Blob de certificado em formato desconhecido.");

        var nonce = protegido.Slice(1, NonceBytes);
        var tag = protegido.Slice(1 + NonceBytes, TagBytes);
        var cifrado = protegido[(1 + NonceBytes + TagBytes)..];
        var dados = new byte[cifrado.Length];

        using var aes = new AesGcm(_chave, TagBytes);
        aes.Decrypt(nonce, cifrado, tag, dados);

        return dados;
    }
}
