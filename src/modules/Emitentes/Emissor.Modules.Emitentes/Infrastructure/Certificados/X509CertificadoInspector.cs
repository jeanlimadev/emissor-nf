using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Emissor.BuildingBlocks;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Emitentes.Domain;

namespace Emissor.Modules.Emitentes.Infrastructure.Certificados;

/// <summary>
/// Abre o pfx e le o titular. O CPF/CNPJ vem, nessa ordem, do otherName do
/// SubjectAltName (OIDs ICP-Brasil 2.16.76.1.3.1 para PF e 2.16.76.1.3.3 para
/// PJ) e, se nao houver, do CN no formato <c>NOME:DOCUMENTO</c>, que e o
/// padrao dos e-CPF/e-CNPJ.
/// </summary>
public sealed class X509CertificadoInspector : ICertificadoInspector
{
    private const string OidSubjectAltName = "2.5.29.17";
    private const string OidIcpPessoaFisica = "2.16.76.1.3.1";
    private const string OidIcpPessoaJuridica = "2.16.76.1.3.3";

    public Result<CertificadoInspecionado> Inspecionar(byte[] pfx, string senha)
    {
        X509Certificate2 certificado;

        try
        {
            certificado = X509CertificateLoader.LoadPkcs12(pfx, senha, X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (CryptographicException)
        {
            return Result<CertificadoInspecionado>.Failure(EmitenteErrors.CertificadoIlegivel);
        }

        using (certificado)
        {
            if (!certificado.HasPrivateKey)
                return Result<CertificadoInspecionado>.Failure(EmitenteErrors.CertificadoSemChavePrivada);

            var commonName = certificado.GetNameInfo(X509NameType.SimpleName, forIssuer: false) ?? string.Empty;
            var documento = DocumentoDoSubjectAltName(certificado) ?? DocumentoDoCommonName(commonName);

            if (documento is null)
                return Result<CertificadoInspecionado>.Failure(EmitenteErrors.CertificadoSemDocumento);

            var titular = TitularDoCommonName(commonName);

            return Result<CertificadoInspecionado>.Success(new CertificadoInspecionado(
                titular,
                documento,
                certificado.Thumbprint,
                new DateTimeOffset(certificado.NotBefore.ToUniversalTime(), TimeSpan.Zero),
                new DateTimeOffset(certificado.NotAfter.ToUniversalTime(), TimeSpan.Zero)));
        }
    }

    private static string TitularDoCommonName(string commonName)
    {
        var separador = commonName.LastIndexOf(':');
        var titular = separador > 0 ? commonName[..separador] : commonName;

        return titular.Trim().Length == 0 ? commonName : titular.Trim();
    }

    private static string? DocumentoDoCommonName(string commonName)
    {
        var separador = commonName.LastIndexOf(':');

        if (separador < 0)
            return null;

        var candidato = DocumentoFiscal.SomenteCaracteresValidos(commonName[(separador + 1)..]);

        return DocumentoFiscal.IsCpfValido(candidato) || DocumentoFiscal.IsCnpjValido(candidato) ? candidato : null;
    }

    private static string? DocumentoDoSubjectAltName(X509Certificate2 certificado)
    {
        var extensao = certificado.Extensions[OidSubjectAltName];

        if (extensao is null)
            return null;

        try
        {
            var reader = new AsnReader(extensao.RawData, AsnEncodingRules.DER);
            var nomes = reader.ReadSequence();
            var otherNameTag = new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true);
            string? cpf = null;

            while (nomes.HasData)
            {
                var tag = nomes.PeekTag();

                if (!tag.HasSameClassAndValue(otherNameTag))
                {
                    nomes.ReadEncodedValue();
                    continue;
                }

                var otherName = nomes.ReadSequence(otherNameTag);
                var oid = otherName.ReadObjectIdentifier();
                var valor = LerValorExplicito(otherName.ReadSequence(otherNameTag));

                if (valor is null)
                    continue;

                if (oid == OidIcpPessoaJuridica)
                {
                    var cnpj = DocumentoFiscal.SomenteCaracteresValidos(valor);

                    if (DocumentoFiscal.IsCnpjValido(cnpj))
                        return cnpj;
                }
                else if (oid == OidIcpPessoaFisica && valor.Length >= 19)
                {
                    // dataNascimento(8) | CPF(11) | NIS(11) | RG(15) | orgao/UF(6)
                    var candidato = valor.Substring(8, 11);

                    if (DocumentoFiscal.IsCpfValido(candidato))
                        cpf = candidato;
                }
            }

            return cpf;
        }
        catch (AsnContentException)
        {
            return null;
        }
    }

    private static string? LerValorExplicito(AsnReader conteudo)
    {
        var tag = conteudo.PeekTag();

        if (tag == Asn1Tag.PrimitiveOctetString)
            return System.Text.Encoding.ASCII.GetString(conteudo.ReadOctetString());

        if (tag.TagClass == TagClass.Universal)
            return conteudo.ReadCharacterString((UniversalTagNumber)tag.TagValue);

        return null;
    }
}
