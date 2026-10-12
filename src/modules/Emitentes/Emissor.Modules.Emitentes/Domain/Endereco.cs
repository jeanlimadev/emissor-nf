using Emissor.BuildingBlocks;

namespace Emissor.Modules.Emitentes.Domain;

/// <summary>Endereco do emitente, como vai no grupo <c>enderEmit</c> da NF-e.</summary>
public sealed record Endereco
{
    public const int LogradouroMaxLength = 60;
    public const int NumeroMaxLength = 60;
    public const int ComplementoMaxLength = 60;
    public const int BairroMaxLength = 60;
    public const int MunicipioMaxLength = 60;

    public static readonly IReadOnlySet<string> Ufs = new HashSet<string>(StringComparer.Ordinal)
    {
        "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA", "PB", "PR",
        "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO"
    };

    private Endereco() { }

    public string Logradouro { get; private init; } = string.Empty;
    public string Numero { get; private init; } = string.Empty;
    public string? Complemento { get; private init; }
    public string Bairro { get; private init; } = string.Empty;
    public string CodigoMunicipio { get; private init; } = string.Empty;
    public string Municipio { get; private init; } = string.Empty;
    public string Uf { get; private init; } = string.Empty;
    public string Cep { get; private init; } = string.Empty;
    public string? Telefone { get; private init; }

    public static Result<Endereco> Criar(
        string? logradouro,
        string? numero,
        string? complemento,
        string? bairro,
        string? codigoMunicipio,
        string? municipio,
        string? uf,
        string? cep,
        string? telefone)
    {
        var logradouroLimpo = Limpar(logradouro);
        var numeroLimpo = Limpar(numero);
        var bairroLimpo = Limpar(bairro);
        var municipioLimpo = Limpar(municipio);
        var ufLimpa = Limpar(uf).ToUpperInvariant();
        var cepLimpo = SomenteDigitos(cep);
        var codigoLimpo = SomenteDigitos(codigoMunicipio);
        var telefoneLimpo = SomenteDigitos(telefone);
        var complementoLimpo = Limpar(complemento);

        if (logradouroLimpo.Length is 0 or > LogradouroMaxLength)
            return Falha("endereco.logradouro_invalido", $"Informe o logradouro com até {LogradouroMaxLength} caracteres.");

        if (numeroLimpo.Length is 0 or > NumeroMaxLength)
            return Falha("endereco.numero_invalido", "Informe o número do endereço (use S/N quando não houver).");

        if (complementoLimpo.Length > ComplementoMaxLength)
            return Falha("endereco.complemento_invalido", $"O complemento deve ter até {ComplementoMaxLength} caracteres.");

        if (bairroLimpo.Length is 0 or > BairroMaxLength)
            return Falha("endereco.bairro_invalido", $"Informe o bairro com até {BairroMaxLength} caracteres.");

        if (codigoLimpo.Length != 7)
            return Falha("endereco.codigo_municipio_invalido", "Informe o código IBGE do município com 7 dígitos.");

        if (municipioLimpo.Length is 0 or > MunicipioMaxLength)
            return Falha("endereco.municipio_invalido", $"Informe o município com até {MunicipioMaxLength} caracteres.");

        if (!Ufs.Contains(ufLimpa))
            return Falha("endereco.uf_invalida", "Informe a sigla da UF.");

        if (cepLimpo.Length != 8)
            return Falha("endereco.cep_invalido", "Informe o CEP com 8 dígitos.");

        if (telefoneLimpo.Length is not (0 or >= 6 and <= 14))
            return Falha("endereco.telefone_invalido", "O telefone deve ter entre 6 e 14 dígitos.");

        return Result<Endereco>.Success(new Endereco
        {
            Logradouro = logradouroLimpo,
            Numero = numeroLimpo,
            Complemento = complementoLimpo.Length == 0 ? null : complementoLimpo,
            Bairro = bairroLimpo,
            CodigoMunicipio = codigoLimpo,
            Municipio = municipioLimpo,
            Uf = ufLimpa,
            Cep = cepLimpo,
            Telefone = telefoneLimpo.Length == 0 ? null : telefoneLimpo
        });
    }

    private static Result<Endereco> Falha(string code, string message) =>
        Result<Endereco>.Failure(Error.Validation(code, message));

    internal static string Limpar(string? valor) => (valor ?? string.Empty).Trim();

    internal static string SomenteDigitos(string? valor) =>
        new((valor ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
}
