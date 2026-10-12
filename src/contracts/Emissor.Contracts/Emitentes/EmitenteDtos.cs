namespace Emissor.Contracts.Emitentes;

/// <summary>Quem emite: pessoa fisica (produtor rural com IE) ou pessoa juridica.</summary>
public enum TipoEmitente
{
    PessoaFisica = 1,
    PessoaJuridica = 2
}

/// <summary>Ambiente da SEFAZ (campo <c>tpAmb</c> da NF-e): 1 producao, 2 homologacao.</summary>
public enum AmbienteSefaz
{
    Producao = 1,
    Homologacao = 2
}

/// <summary>Codigo de Regime Tributario (campo <c>CRT</c> da NF-e).</summary>
public enum RegimeTributario
{
    SimplesNacional = 1,
    SimplesNacionalExcessoSublimite = 2,
    RegimeNormal = 3,
    MicroempreendedorIndividual = 4
}

public sealed record EnderecoDto(
    string Logradouro,
    string Numero,
    string? Complemento,
    string Bairro,
    string CodigoMunicipio,
    string Municipio,
    string Uf,
    string Cep,
    string? Telefone);

public sealed record EmitenteRequest(
    TipoEmitente Tipo,
    string Documento,
    string InscricaoEstadual,
    string Nome,
    string? NomeFantasia,
    RegimeTributario RegimeTributario,
    AmbienteSefaz Ambiente,
    EnderecoDto Endereco,
    string? Email);

public sealed record EmitenteResponse(
    Guid Id,
    TipoEmitente Tipo,
    string Documento,
    string InscricaoEstadual,
    string Nome,
    string? NomeFantasia,
    RegimeTributario RegimeTributario,
    AmbienteSefaz Ambiente,
    EnderecoDto Endereco,
    string? Email,
    bool Ativo,
    bool PossuiCertificado,
    DateTimeOffset? CertificadoValidoAte,
    bool EmContingencia,
    DateTimeOffset? ContingenciaDesde,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm);

/// <summary>Liga a contingencia SVC-AN do emitente. A justificativa vai no XML de cada nota emitida em contingencia (<c>xJust</c>).</summary>
public sealed record ContingenciaRequest(string Justificativa);

/// <summary>Aviso de certificado a vencer (webhook <c>emitente.certificado_vencendo</c>).</summary>
public sealed record CertificadoVencendoDto(Guid EmitenteId, string Nome, string Documento, DateTimeOffset ValidoAte, int DiasRestantes);

public sealed record CertificadoResponse(
    string Titular,
    string Documento,
    string Thumbprint,
    DateTimeOffset ValidoDe,
    DateTimeOffset ValidoAte,
    DateTimeOffset EnviadoEm);

public sealed record SerieRequest(int Numero, long ProximoNumero);

public sealed record SerieResponse(Guid Id, int Modelo, int Numero, long ProximoNumero, bool Ativa, DateTimeOffset CriadaEm);
