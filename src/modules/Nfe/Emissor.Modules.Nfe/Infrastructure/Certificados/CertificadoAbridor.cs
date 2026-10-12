using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Emissor.BuildingBlocks;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Emitentes.Domain;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;

namespace Emissor.Modules.Nfe.Infrastructure.Certificados;

/// <summary>
/// Descriptografa pfx e senha com a chave mestra e carrega o certificado com a
/// chave privada so em memoria (EphemeralKeySet: nada vai para o keystore do
/// sistema). A senha em claro vive o tempo de uma chamada.
/// </summary>
public sealed class CertificadoAbridor(ICertificadoProtector protector) : ICertificadoAbridor
{
    public Result<X509Certificate2> Abrir(Certificado certificado)
    {
        try
        {
            var pfx = protector.Abrir(certificado.PfxProtegido);
            var senha = Encoding.UTF8.GetString(protector.Abrir(certificado.SenhaProtegida));

            var x509 = X509CertificateLoader.LoadPkcs12(pfx, senha, X509KeyStorageFlags.EphemeralKeySet);

            if (!x509.HasPrivateKey)
            {
                x509.Dispose();
                return Result<X509Certificate2>.Failure(NfeErrors.CertificadoIlegivel);
            }

            return Result<X509Certificate2>.Success(x509);
        }
        catch (CryptographicException)
        {
            return Result<X509Certificate2>.Failure(NfeErrors.CertificadoIlegivel);
        }
    }
}

/// <summary>cNF aleatorio de 8 digitos, nunca igual ao numero da nota (NT 2019.001).</summary>
public sealed class GeradorCodigoNumericoAleatorio : IGeradorCodigoNumerico
{
    public string Gerar(long numeroNota)
    {
        while (true)
        {
            var codigo = RandomNumberGenerator.GetInt32(0, 100_000_000);

            if (codigo != numeroNota)
                return codigo.ToString("00000000");
        }
    }
}
