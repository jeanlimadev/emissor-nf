namespace Emissor.BuildingBlocks;

/// <summary>
/// Digito verificador da Inscricao Estadual. Cada UF tem o seu algoritmo;
/// aqui esta o do RJ (8 digitos, modulo 11 com pesos 2,7,6,5,4,3,2), a UF
/// atendida hoje. Para as demais, so se confere o tamanho (2 a 14 digitos):
/// o algoritmo da UF entra aqui quando um emitente dela aparecer. A SEFAZ
/// rejeita IE errada com cStat 210; conferir antes poupa um numero da serie.
/// </summary>
public static class InscricaoEstadual
{
    public static string SomenteDigitos(string? valor) =>
        new((valor ?? string.Empty).Where(char.IsAsciiDigit).ToArray());

    /// <summary>Devolve true quando a IE serve para a UF. "ISENTO" nao passa por aqui: e tratado pelo indicador de IE.</summary>
    public static bool IsValida(string uf, string? inscricaoEstadual)
    {
        var ie = SomenteDigitos(inscricaoEstadual);

        if (ie.Length is < 2 or > 14)
            return false;

        return uf.ToUpperInvariant() switch
        {
            "RJ" => IsValidaRj(ie),
            _ => true
        };
    }

    private static bool IsValidaRj(string ie)
    {
        if (ie.Length != 8)
            return false;

        int[] pesos = [2, 7, 6, 5, 4, 3, 2];
        var soma = 0;

        for (var i = 0; i < 7; i++)
            soma += (ie[i] - '0') * pesos[i];

        var resto = soma % 11;
        var dv = resto < 2 ? 0 : 11 - resto;

        return ie[7] - '0' == dv;
    }
}
