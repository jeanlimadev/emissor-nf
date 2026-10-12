using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Domain;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using FluentValidation;

namespace Emissor.Modules.Nfe.Application.Emissao;

/// <summary>O que sai do montador: a nota pronta para transmitir.</summary>
public sealed record NfeAssinada(ChaveAcesso Chave, DateTimeOffset DataEmissao, string Xml);

/// <summary>
/// O caminho offline da emissao, na ordem que a SEFAZ espera:
/// pedido valido -> chave de acesso -> XML -> assinatura -> validacao XSD.
/// A fase 2 usa este mesmo montador antes de transmitir; a previa o usa sozinho.
/// </summary>
public sealed class MontadorDeNfe(
    IValidator<EmissaoNfeRequest> validador,
    INfeXmlBuilder xmlBuilder,
    INfeAssinador assinador,
    INfeXsdValidator xsdValidator,
    ICertificadoAbridor certificados,
    IGeradorCodigoNumerico codigoNumerico,
    IClock clock)
{
    public async Task<Result<NfeAssinada>> MontarAsync(Emitente emitente, int serie, long numero, EmissaoNfeRequest pedido, CancellationToken cancellationToken)
    {
        var validacao = await validador.ValidateAsync(pedido, cancellationToken);

        if (!validacao.IsValid)
            return Result<NfeAssinada>.Failure(EmissaoNfeRequestValidator.ParaErro(validacao));

        if (!emitente.Ativo)
            return Result<NfeAssinada>.Failure(NfeErrors.EmitenteInativo);

        if (emitente.Certificado is null)
            return Result<NfeAssinada>.Failure(NfeErrors.EmitenteSemCertificado);

        var agora = clock.UtcNow;

        if (emitente.Certificado.ValidoAte <= agora)
            return Result<NfeAssinada>.Failure(NfeErrors.CertificadoVencido);

        // dhEmi no fuso do emitente: o AAMM da chave e o horario do XML tem que concordar entre si e com a SEFAZ.
        var dataEmissao = Uf.NoFuso(pedido.Identificacao.DataEmissao ?? agora, emitente.Endereco.Uf);

        var chave = ChaveAcesso.Criar(
            Uf.Codigo(emitente.Endereco.Uf),
            dataEmissao,
            emitente.Documento,
            serie,
            numero,
            codigoNumerico.Gerar(numero),
            emitente.EmContingencia ? ChaveAcesso.TipoEmissaoSvcAn : ChaveAcesso.TipoEmissaoNormal);

        if (chave.IsFailure)
            return chave.FailAs<NfeAssinada>();

        var xml = xmlBuilder.Montar(new NfeMontagem(emitente, chave.Value!, dataEmissao, pedido));

        if (xml.IsFailure)
            return xml.FailAs<NfeAssinada>();

        var certificado = certificados.Abrir(emitente.Certificado);

        if (certificado.IsFailure)
            return certificado.FailAs<NfeAssinada>();

        string assinado;

        using (certificado.Value)
            assinado = assinador.Assinar(xml.Value!, certificado.Value!);

        // Valida o XML ja assinado: o schema da NF-e exige o elemento Signature.
        var schema = xsdValidator.ValidarNfe(assinado);

        if (schema.IsFailure)
            return schema.FailAs<NfeAssinada>();

        return Result<NfeAssinada>.Success(new NfeAssinada(chave.Value!, dataEmissao, assinado));
    }
}
