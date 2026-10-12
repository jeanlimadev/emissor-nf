using System.Globalization;
using Emissor.BuildingBlocks;
using Emissor.Modules.Nfe.Domain;

namespace Emissor.Modules.Nfe.Infrastructure.Xml;

/// <summary>
/// Junta os problemas encontrados durante o mapeamento em vez de parar no
/// primeiro: o cliente recebe a lista inteira (campo -> mensagens) e corrige de
/// uma vez. Os campos usam o caminho do JSON do pedido (ex.:
/// <c>itens[2].impostos.icms.baseCalculo</c>).
/// </summary>
internal sealed class MapeamentoContexto
{
    private readonly Dictionary<string, List<string>> _erros = new(StringComparer.Ordinal);

    public bool TemErros => _erros.Count > 0;

    public void Erro(string campo, string mensagem)
    {
        if (!_erros.TryGetValue(campo, out var lista))
            _erros[campo] = lista = [];

        lista.Add(mensagem);
    }

    /// <summary>Campo obrigatorio para o CST/situacao escolhida. Devolve 0 so para o mapeamento seguir; o erro fica registrado.</summary>
    public decimal Exigir(decimal? valor, string campo, string porque)
    {
        if (valor is null)
            Erro(campo, $"Obrigatório {porque}.");

        return valor ?? 0m;
    }

    public TEnum Exigir<TEnum>(TEnum? valor, string campo, string porque) where TEnum : struct, Enum
    {
        if (valor is null)
            Erro(campo, $"Obrigatório {porque}.");

        return valor ?? default;
    }

    public string Exigir(string? valor, string campo, string porque)
    {
        if (string.IsNullOrWhiteSpace(valor))
            Erro(campo, $"Obrigatório {porque}.");

        return (valor ?? string.Empty).Trim();
    }

    /// <summary>Tenta ler um codigo (CST, CSOSN...) como membro do enum da biblioteca, com o prefixo que ela usa.</summary>
    public TEnum Codigo<TEnum>(string? codigo, string prefixo, string campo, string descricao) where TEnum : struct, Enum
    {
        var limpo = (codigo ?? string.Empty).Trim();

        if (limpo.Length > 0 && Enum.TryParse<TEnum>(prefixo + limpo, ignoreCase: false, out var valor) && Enum.IsDefined(valor))
            return valor;

        Erro(campo, $"{descricao} '{limpo}' não é reconhecido.");
        return default;
    }

    public Result<T> Falhar<T>() =>
        Result<T>.Failure(NfeErrors.PedidoInvalido.ComDetalhes(
            _erros.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal)));

    public static string Item(int indice, string resto) =>
        string.Create(CultureInfo.InvariantCulture, $"itens[{indice}].{resto}");
}
