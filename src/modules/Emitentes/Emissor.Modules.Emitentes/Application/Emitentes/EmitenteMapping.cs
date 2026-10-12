using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;
using Emissor.Modules.Emitentes.Domain;

namespace Emissor.Modules.Emitentes.Application.Emitentes;

internal static class EmitenteMapping
{
    internal static Result<Emitente.Dados> ToDados(EmitenteRequest request)
    {
        if (request.Endereco is null)
            return Result<Emitente.Dados>.Failure(Error.Validation("endereco.obrigatorio", "Informe o endereço do emitente."));

        var endereco = Endereco.Criar(
            request.Endereco.Logradouro,
            request.Endereco.Numero,
            request.Endereco.Complemento,
            request.Endereco.Bairro,
            request.Endereco.CodigoMunicipio,
            request.Endereco.Municipio,
            request.Endereco.Uf,
            request.Endereco.Cep,
            request.Endereco.Telefone);

        if (endereco.IsFailure)
            return endereco.FailAs<Emitente.Dados>();

        return Result<Emitente.Dados>.Success(new Emitente.Dados(
            request.Tipo,
            request.Documento,
            request.InscricaoEstadual,
            request.Nome,
            request.NomeFantasia,
            request.RegimeTributario,
            request.Ambiente,
            endereco.Value!,
            request.Email));
    }

    internal static EmitenteResponse ToResponse(Emitente emitente) =>
        new(
            emitente.Id,
            emitente.Tipo,
            emitente.Documento,
            emitente.InscricaoEstadual,
            emitente.Nome,
            emitente.NomeFantasia,
            emitente.RegimeTributario,
            emitente.Ambiente,
            new EnderecoDto(
                emitente.Endereco.Logradouro,
                emitente.Endereco.Numero,
                emitente.Endereco.Complemento,
                emitente.Endereco.Bairro,
                emitente.Endereco.CodigoMunicipio,
                emitente.Endereco.Municipio,
                emitente.Endereco.Uf,
                emitente.Endereco.Cep,
                emitente.Endereco.Telefone),
            emitente.Email,
            emitente.Ativo,
            emitente.Certificado is not null,
            emitente.Certificado?.ValidoAte,
            emitente.EmContingencia,
            emitente.ContingenciaDesde,
            emitente.CriadoEm,
            emitente.AtualizadoEm);

    internal static CertificadoResponse ToResponse(Certificado certificado) =>
        new(
            certificado.Titular,
            certificado.Documento,
            certificado.Thumbprint,
            certificado.ValidoDe,
            certificado.ValidoAte,
            certificado.EnviadoEm);

    internal static SerieResponse ToResponse(Serie serie) =>
        new(serie.Id, serie.Modelo, serie.Numero, serie.ProximoNumero, serie.Ativa, serie.CriadaEm);
}
