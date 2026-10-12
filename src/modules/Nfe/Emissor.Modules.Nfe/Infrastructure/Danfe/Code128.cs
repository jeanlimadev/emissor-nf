using System.Globalization;
using System.Text;

namespace Emissor.Modules.Nfe.Infrastructure.Danfe;

/// <summary>
/// Codigo de barras CODE-128C da chave de acesso, como o Manual de Orientacao
/// exige na DANFE (44 digitos, so numeros, pares de digitos por simbolo).
/// Gera um SVG vetorial: barras pretas sobre fundo branco, com zona de
/// silencio de 10 modulos de cada lado.
/// </summary>
public static class Code128
{
    private const int StartC = 105;
    private const int Stop = 106;
    private const int ZonaDeSilencio = 10;

    // Larguras (barra, espaco, barra, espaco, barra, espaco) de cada simbolo, conforme a ISO/IEC 15417.
    private static readonly string[] Padroes =
    [
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    ];

    /// <summary>Os valores dos simbolos: Start C, pares de digitos, digito de verificacao (modulo 103) e Stop.</summary>
    public static IReadOnlyList<int> Simbolos(string digitos)
    {
        if (digitos.Length == 0 || digitos.Length % 2 != 0 || !digitos.All(char.IsAsciiDigit))
            throw new ArgumentException("CODE-128C exige uma quantidade par de dígitos.", nameof(digitos));

        var simbolos = new List<int> { StartC };

        for (var i = 0; i < digitos.Length; i += 2)
            simbolos.Add(int.Parse(digitos.AsSpan(i, 2), CultureInfo.InvariantCulture));

        var soma = StartC;

        for (var i = 1; i < simbolos.Count; i++)
            soma += simbolos[i] * i;

        simbolos.Add(soma % 103);
        simbolos.Add(Stop);

        return simbolos;
    }

    /// <summary>Larguras alternadas (barra, espaco, barra...) de todo o codigo, em modulos.</summary>
    public static IReadOnlyList<int> Larguras(string digitos) =>
        Simbolos(digitos).SelectMany(s => Padroes[s].Select(c => c - '0')).ToList();

    public static string Svg(string digitos, int alturaModulos = 40)
    {
        var larguras = Larguras(digitos);
        var larguraTotal = larguras.Sum() + 2 * ZonaDeSilencio;
        var svg = new StringBuilder();

        svg.Append(CultureInfo.InvariantCulture, $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {larguraTotal} {alturaModulos}" shape-rendering="crispEdges">""");
        svg.Append(CultureInfo.InvariantCulture, $"""<rect width="{larguraTotal}" height="{alturaModulos}" fill="#fff"/>""");

        var x = ZonaDeSilencio;

        for (var i = 0; i < larguras.Count; i++)
        {
            if (i % 2 == 0)
                svg.Append(CultureInfo.InvariantCulture, $"""<rect x="{x}" y="0" width="{larguras[i]}" height="{alturaModulos}" fill="#000"/>""");

            x += larguras[i];
        }

        svg.Append("</svg>");

        return svg.ToString();
    }

    /// <summary>Conferencia da tabela: todo simbolo tem 11 modulos, e o Stop tem 13.</summary>
    public static bool TabelaConsistente() =>
        Padroes.Take(106).All(p => p.Sum(c => c - '0') == 11) && Padroes[106].Sum(c => c - '0') == 13;
}
