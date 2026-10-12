using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using Emissor.Modules.Nfe.Application.Ports;

namespace Emissor.Modules.Nfe.Infrastructure.Assinatura;

/// <summary>
/// Assinatura digital da NF-e (Manual de Orientacao do Contribuinte, secao de
/// assinatura): XMLDSig enveloped sobre o <c>infNFe</c>, referenciado pelo
/// atributo <c>Id</c>, com canonicalizacao C14N, digest SHA-1 e RSA-SHA1. A
/// SEFAZ ainda exige SHA-1 aqui; nao e escolha nossa. O elemento
/// <c>Signature</c> entra como ultimo filho de <c>NFe</c>.
/// </summary>
public sealed class XmlDsigNfeAssinador : INfeAssinador
{
    public const string NamespaceNfe = "http://www.portalfiscal.inf.br/nfe";

    public string Assinar(string xmlNfe, X509Certificate2 certificado) => AssinarElemento(xmlNfe, "infNFe", certificado);

    public string AssinarElemento(string xml, string nomeElementoComId, X509Certificate2 certificado)
    {
        var documento = new XmlDocument { PreserveWhitespace = true };
        documento.LoadXml(xml);

        var elemento = documento.GetElementsByTagName(nomeElementoComId, NamespaceNfe).Cast<XmlElement>().SingleOrDefault()
            ?? throw new InvalidOperationException($"XML sem o elemento {nomeElementoComId}.");

        var id = elemento.GetAttribute("Id");

        if (string.IsNullOrEmpty(id))
            throw new InvalidOperationException($"{nomeElementoComId} sem o atributo Id.");

        using var chavePrivada = certificado.GetRSAPrivateKey()
            ?? throw new InvalidOperationException("O certificado não tem chave privada RSA.");

        var assinatura = new SignedXml(documento) { SigningKey = chavePrivada };
        assinatura.SignedInfo!.CanonicalizationMethod = SignedXml.XmlDsigC14NTransformUrl;
        assinatura.SignedInfo.SignatureMethod = SignedXml.XmlDsigRSASHA1Url;

        var referencia = new Reference("#" + id) { DigestMethod = SignedXml.XmlDsigSHA1Url };
        referencia.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        referencia.AddTransform(new XmlDsigC14NTransform());
        assinatura.AddReference(referencia);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificado));
        assinatura.KeyInfo = keyInfo;

        assinatura.ComputeSignature();

        // O Signature entra logo apos o elemento assinado, no pai dele (NFe, evento, inutNFe).
        var pai = (XmlElement)elemento.ParentNode!;
        pai.InsertAfter(documento.ImportNode(assinatura.GetXml(), deep: true), elemento);

        return documento.DocumentElement!.OuterXml;
    }

    /// <summary>Confere a assinatura de um XML de NF-e (usado nos testes e ao receber XML de fora).</summary>
    public static bool Verificar(string xmlAssinado)
    {
        var documento = new XmlDocument { PreserveWhitespace = true };
        documento.LoadXml(xmlAssinado);

        var elementoAssinatura = documento.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl)
            .Cast<XmlElement>().SingleOrDefault();

        if (elementoAssinatura is null)
            return false;

        var assinatura = new SignedXml(documento);
        assinatura.LoadXml(elementoAssinatura);

        var certificado = assinatura.KeyInfo.OfType<KeyInfoX509Data>()
            .SelectMany(k => k.Certificates?.Cast<X509Certificate2>() ?? [])
            .FirstOrDefault();

        return certificado is not null && assinatura.CheckSignature(certificado, verifySignatureOnly: true);
    }
}
