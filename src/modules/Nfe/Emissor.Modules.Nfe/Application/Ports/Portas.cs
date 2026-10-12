using System.Security.Cryptography.X509Certificates;
using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Domain;
using Emissor.Modules.Nfe.Domain;

namespace Emissor.Modules.Nfe.Application.Ports;

/// <summary>Tudo o que o montador precisa para produzir o XML de uma nota.</summary>
/// <param name="DataEmissao">Ja no fuso do emitente; vai para <c>dhEmi</c> e para o AAMM da chave.</param>
public sealed record NfeMontagem(
    Emitente Emitente,
    ChaveAcesso Chave,
    DateTimeOffset DataEmissao,
    EmissaoNfeRequest Pedido);

/// <summary>
/// Converte o pedido de emissao no XML da NF-e (elemento <c>NFe</c>, ainda sem
/// assinatura). E a unica porta que conhece a biblioteca de NF-e.
/// </summary>
public interface INfeXmlBuilder
{
    Result<string> Montar(NfeMontagem montagem);
}

/// <summary>Valida o XML da NF-e contra os schemas oficiais do layout 4.00.</summary>
public interface INfeXsdValidator
{
    Result ValidarNfe(string xml);

    /// <summary>Valida o <c>envEvento</c> de cancelamento (envEventoCancNFe) ou carta de correcao (envCCe).</summary>
    Result ValidarEvento(string xmlEnvEvento, Contracts.Nfe.TipoEventoNfe tipo);

    Result ValidarInutilizacao(string xmlInutNfe);
}

/// <summary>Assina com o certificado do emitente (XMLDSig enveloped, RSA-SHA1, como a SEFAZ exige).</summary>
public interface INfeAssinador
{
    /// <summary>Assina o <c>infNFe</c>; o <c>Signature</c> entra como ultimo filho de <c>NFe</c>.</summary>
    string Assinar(string xmlNfe, X509Certificate2 certificado);

    /// <summary>Assina o elemento com <c>Id</c> indicado (<c>infEvento</c>, <c>infInut</c>); o <c>Signature</c> entra apos ele, no pai.</summary>
    string AssinarElemento(string xml, string nomeElementoComId, X509Certificate2 certificado);
}

/// <summary>Abre o certificado A1 guardado (descriptografa pfx e senha e carrega a chave privada).</summary>
public interface ICertificadoAbridor
{
    Result<X509Certificate2> Abrir(Certificado certificado);
}

/// <summary>Gera o codigo numerico (cNF) da chave: 8 digitos aleatorios, diferentes do numero da nota.</summary>
public interface IGeradorCodigoNumerico
{
    string Gerar(long numeroNota);
}
