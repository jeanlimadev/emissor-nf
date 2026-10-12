using Emissor.BuildingBlocks;

namespace Emissor.Modules.Nfe.Domain;

/// <summary>
/// A chave de acesso da NF-e: 44 digitos que identificam a nota no Brasil
/// inteiro. Composicao (posicoes):
/// <code>
/// cUF(2) AAMM(4) CNPJ/CPF(14) mod(2) serie(3) nNF(9) tpEmis(1) cNF(8) cDV(1)
/// </code>
/// O CPF do produtor rural entra com zeros a esquerda ate 14 posicoes (NT 2018.001).
/// O digito verificador e modulo 11 com pesos 2 a 9 da direita para a esquerda.
/// </summary>
public sealed record ChaveAcesso
{
    public const int Tamanho = 44;
    public const int ModeloNfe = 55;
    public const int TipoEmissaoNormal = 1;
    /// <summary>Contingencia SVC-AN (Sefaz Virtual de Contingencia do Ambiente Nacional).</summary>
    public const int TipoEmissaoSvcAn = 6;

    private ChaveAcesso(string valor)
    {
        Valor = valor;
    }

    public string Valor { get; }

    public int CodigoUf => int.Parse(Valor.AsSpan(0, 2));
    public string AnoMes => Valor.Substring(2, 4);
    public string Documento => Valor.Substring(6, 14);
    public int Modelo => int.Parse(Valor.AsSpan(20, 2));
    public int Serie => int.Parse(Valor.AsSpan(22, 3));
    public long Numero => long.Parse(Valor.AsSpan(25, 9));
    public int TipoEmissao => int.Parse(Valor.AsSpan(34, 1));
    public string CodigoNumerico => Valor.Substring(35, 8);
    public int DigitoVerificador => Valor[43] - '0';

    /// <summary>O valor do atributo <c>Id</c> do <c>infNFe</c>: "NFe" + chave.</summary>
    public string Id => "NFe" + Valor;

    public static Result<ChaveAcesso> Criar(
        int codigoUf,
        DateTimeOffset dataEmissao,
        string documentoEmitente,
        int serie,
        long numero,
        string codigoNumerico,
        int tipoEmissao = TipoEmissaoNormal)
    {
        if (codigoUf is < 11 or > 53)
            return Result<ChaveAcesso>.Failure(NfeErrors.ChaveUfInvalida);

        // CPF: 11 digitos. CNPJ: 14 posicoes, e as 12 primeiras (raiz + ordem) podem ter letras maiusculas
        // (CNPJ alfanumerico, NT 2025.001); os dois digitos verificadores sao sempre numericos.
        var documentoValido = documentoEmitente.Length switch
        {
            11 => documentoEmitente.All(char.IsAsciiDigit),
            14 => documentoEmitente.Take(12).All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)) && documentoEmitente.Skip(12).All(char.IsAsciiDigit),
            _ => false
        };

        if (!documentoValido)
            return Result<ChaveAcesso>.Failure(NfeErrors.ChaveDocumentoInvalido);

        if (serie is < 0 or > 999)
            return Result<ChaveAcesso>.Failure(NfeErrors.ChaveSerieInvalida);

        if (numero is < 1 or > 999_999_999)
            return Result<ChaveAcesso>.Failure(NfeErrors.ChaveNumeroInvalido);

        if (codigoNumerico.Length != 8 || !codigoNumerico.All(char.IsAsciiDigit))
            return Result<ChaveAcesso>.Failure(NfeErrors.ChaveCodigoNumericoInvalido);

        // cNF igual ao numero da nota e rejeitado (regra da NT 2019.001).
        if (long.Parse(codigoNumerico) == numero)
            return Result<ChaveAcesso>.Failure(NfeErrors.ChaveCodigoNumericoIgualAoNumero);

        var semDv =
            codigoUf.ToString("00") +
            dataEmissao.ToString("yyMM") +
            documentoEmitente.PadLeft(14, '0') +
            ModeloNfe.ToString("00") +
            serie.ToString("000") +
            numero.ToString("000000000") +
            tipoEmissao.ToString("0") +
            codigoNumerico;

        return Result<ChaveAcesso>.Success(new ChaveAcesso(semDv + CalcularDigitoVerificador(semDv)));
    }

    /// <summary>Le uma chave pronta (ex.: nota referenciada) conferindo tamanho e DV.</summary>
    public static Result<ChaveAcesso> Parse(string? valor)
    {
        var limpo = (valor ?? string.Empty).Trim();

        if (limpo.StartsWith("NFe", StringComparison.OrdinalIgnoreCase))
            limpo = limpo[3..];

        // Mesmo formato do atributo Id do infNFe: NFe[0-9]{6}[0-9A-Z]{12}[0-9]{26}.
        if (limpo.Length != Tamanho || !limpo.Take(6).All(char.IsAsciiDigit)
            || !limpo.Skip(6).Take(12).All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c))
            || !limpo.Skip(18).All(char.IsAsciiDigit))
            return Result<ChaveAcesso>.Failure(NfeErrors.ChaveInvalida);

        if (CalcularDigitoVerificador(limpo[..43]) != limpo[43] - '0')
            return Result<ChaveAcesso>.Failure(NfeErrors.ChaveDigitoVerificadorInvalido);

        return Result<ChaveAcesso>.Success(new ChaveAcesso(limpo));
    }

    /// <summary>
    /// Modulo 11: pesos 2..9 repetidos da direita para a esquerda; resto 0 ou 1 da DV 0.
    /// Letra do CNPJ alfanumerico vale o codigo ASCII menos 48 (A=17 ... Z=42), como na NT 2025.001.
    /// </summary>
    public static int CalcularDigitoVerificador(string quarentaETresDigitos)
    {
        if (quarentaETresDigitos.Length != Tamanho - 1)
            throw new ArgumentException("A chave sem DV tem 43 posições.", nameof(quarentaETresDigitos));

        var soma = 0;
        var peso = 2;

        for (var i = quarentaETresDigitos.Length - 1; i >= 0; i--)
        {
            soma += (quarentaETresDigitos[i] - '0') * peso;
            peso = peso == 9 ? 2 : peso + 1;
        }

        var resto = soma % 11;

        return resto < 2 ? 0 : 11 - resto;
    }

    public override string ToString() => Valor;
}
