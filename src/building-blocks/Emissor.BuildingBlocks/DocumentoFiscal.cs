namespace Emissor.BuildingBlocks;

/// <summary>
/// Validacao de CPF e CNPJ. O CNPJ aceita o formato alfanumerico (vigente desde
/// julho de 2026): os 12 primeiros caracteres podem ser letras maiusculas ou
/// digitos, os 2 digitos verificadores continuam numericos, e no calculo cada
/// caractere vale o seu codigo ASCII menos 48 (0-9 valem 0-9, A-Z valem 17-42).
/// </summary>
public static class DocumentoFiscal
{
    public static string SomenteCaracteresValidos(string? valor) =>
        new((valor ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    public static bool IsCpfValido(string? cpf)
    {
        var digitos = SomenteCaracteresValidos(cpf);

        if (digitos.Length != 11 || !digitos.All(char.IsAsciiDigit))
            return false;

        if (digitos.Distinct().Count() == 1)
            return false;

        var dv1 = DigitoCpf(digitos, 9, 10);
        var dv2 = DigitoCpf(digitos, 10, 11);

        return digitos[9] - '0' == dv1 && digitos[10] - '0' == dv2;
    }

    private static int DigitoCpf(string digitos, int tamanho, int pesoInicial)
    {
        var soma = 0;
        for (var i = 0; i < tamanho; i++)
            soma += (digitos[i] - '0') * (pesoInicial - i);

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }

    private static readonly int[] PesosCnpjDv1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
    private static readonly int[] PesosCnpjDv2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

    public static bool IsCnpjValido(string? cnpj)
    {
        var valor = SomenteCaracteresValidos(cnpj);

        if (valor.Length != 14)
            return false;

        if (!valor[..12].All(c => char.IsAsciiDigit(c) || char.IsAsciiLetterUpper(c)))
            return false;

        if (!valor[12..].All(char.IsAsciiDigit))
            return false;

        if (valor.Distinct().Count() == 1)
            return false;

        var dv1 = DigitoCnpj(valor, PesosCnpjDv1);
        var dv2 = DigitoCnpj(valor, PesosCnpjDv2);

        return valor[12] - '0' == dv1 && valor[13] - '0' == dv2;
    }

    private static int DigitoCnpj(string valor, int[] pesos)
    {
        var soma = 0;
        for (var i = 0; i < pesos.Length; i++)
            soma += (valor[i] - '0') * pesos[i];

        var resto = soma % 11;
        return resto < 2 ? 0 : 11 - resto;
    }
}
