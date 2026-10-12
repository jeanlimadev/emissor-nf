namespace Emissor.Modules.Nfe.Domain;

/// <summary>Codigo IBGE da UF (campo <c>cUF</c>) e o fuso horario que vai no <c>dhEmi</c>.</summary>
public static class Uf
{
    private static readonly Dictionary<string, int> Codigos = new(StringComparer.Ordinal)
    {
        ["RO"] = 11, ["AC"] = 12, ["AM"] = 13, ["RR"] = 14, ["PA"] = 15, ["AP"] = 16, ["TO"] = 17,
        ["MA"] = 21, ["PI"] = 22, ["CE"] = 23, ["RN"] = 24, ["PB"] = 25, ["PE"] = 26, ["AL"] = 27, ["SE"] = 28, ["BA"] = 29,
        ["MG"] = 31, ["ES"] = 32, ["RJ"] = 33, ["SP"] = 35,
        ["PR"] = 41, ["SC"] = 42, ["RS"] = 43,
        ["MS"] = 50, ["MT"] = 51, ["GO"] = 52, ["DF"] = 53
    };

    // O Brasil nao tem horario de verao desde 2019, entao o deslocamento por UF e fixo.
    private static readonly Dictionary<string, TimeSpan> Fusos = new(StringComparer.Ordinal)
    {
        ["AC"] = TimeSpan.FromHours(-5),
        ["AM"] = TimeSpan.FromHours(-4), ["RR"] = TimeSpan.FromHours(-4), ["RO"] = TimeSpan.FromHours(-4),
        ["MT"] = TimeSpan.FromHours(-4), ["MS"] = TimeSpan.FromHours(-4)
    };

    public static readonly TimeSpan FusoBrasilia = TimeSpan.FromHours(-3);

    public static int Codigo(string uf) => Codigos[uf];

    public static TimeSpan Fuso(string uf) => Fusos.GetValueOrDefault(uf, FusoBrasilia);

    /// <summary>Converte um instante para o relogio da UF: a SEFAZ confere o <c>dhEmi</c> com o fuso do emitente.</summary>
    public static DateTimeOffset NoFuso(DateTimeOffset instante, string uf) => instante.ToOffset(Fuso(uf));
}
