namespace Emissor.BuildingBlocks;

/// <summary>
/// Recorte pedido a uma listagem: pagina 1-based e tamanho de pagina. Ausente
/// (<c>null</c>) significa sem recorte: a listagem devolve tudo o que o filtro
/// alcanca. Mesmo contrato do ERP (ADR-083 de la).
/// </summary>
public sealed record PageRequest(int Page, int PageSize)
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;

    public int Skip => (Page - 1) * PageSize;
    public int Take => PageSize;

    /// <returns>
    /// <c>false</c> quando os valores nao servem: pagina menor que 1 ou alta
    /// demais, ou tamanho fora de <c>[1, MaxPageSize]</c>. Recusar e deliberado:
    /// limitar em silencio faria o cliente paginar sobre um tamanho que nao pediu.
    /// </returns>
    public static bool TryCreate(int? page, int? pageSize, out PageRequest? request, out bool pageSizeInvalid)
    {
        request = null;
        pageSizeInvalid = false;

        if (page is null && pageSize is null)
            return true;

        if (pageSize is < 1 or > MaxPageSize)
        {
            pageSizeInvalid = true;
            return false;
        }

        if (page is < 1)
            return false;

        var resolvedPage = page ?? DefaultPage;
        var resolvedPageSize = pageSize ?? DefaultPageSize;

        if ((long)(resolvedPage - 1) * resolvedPageSize > int.MaxValue)
            return false;

        request = new PageRequest(resolvedPage, resolvedPageSize);
        return true;
    }

    public static readonly Error InvalidPage =
        Error.Validation("paging.pagina_invalida", "A página deve ser maior ou igual a 1.");

    public static readonly Error InvalidPageSize =
        Error.Validation("paging.tamanho_invalido", $"O tamanho da página deve estar entre 1 e {MaxPageSize}.");
}

/// <summary>Itens da pagina e o total do filtro aplicado (nao o da tabela).</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total)
{
    public static PagedResult<T> Whole(IReadOnlyList<T> items) => new(items, items.Count);
}

/// <summary>Envelope de resposta das listagens. Sempre presente, inclusive sem recorte.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public static PagedResponse<T> From(PageRequest? request, IReadOnlyList<T> items, int total) =>
        new(items, request?.Page ?? PageRequest.DefaultPage, request?.PageSize ?? total, total);

    public static PagedResponse<T> From(PageRequest? request, PagedResult<T> result) =>
        From(request, result.Items, result.Total);
}
