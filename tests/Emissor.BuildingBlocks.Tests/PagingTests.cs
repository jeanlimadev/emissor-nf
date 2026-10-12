using Emissor.BuildingBlocks;

namespace Emissor.BuildingBlocks.Tests;

[TestClass]
public sealed class PagingTests
{
    [TestMethod]
    public void Sem_parametros_nao_ha_recorte()
    {
        Assert.IsTrue(PageRequest.TryCreate(null, null, out var request, out _));
        Assert.IsNull(request);
    }

    [TestMethod]
    public void So_page_assume_tamanho_padrao()
    {
        Assert.IsTrue(PageRequest.TryCreate(3, null, out var request, out _));
        Assert.AreEqual(3, request!.Page);
        Assert.AreEqual(PageRequest.DefaultPageSize, request.PageSize);
        Assert.AreEqual(2 * PageRequest.DefaultPageSize, request.Skip);
    }

    [TestMethod]
    public void Tamanho_acima_do_teto_e_recusado_e_nao_limitado()
    {
        Assert.IsFalse(PageRequest.TryCreate(1, PageRequest.MaxPageSize + 1, out _, out var pageSizeInvalid));
        Assert.IsTrue(pageSizeInvalid);
    }

    [TestMethod]
    public void Pagina_zero_e_recusada()
    {
        Assert.IsFalse(PageRequest.TryCreate(0, 10, out _, out var pageSizeInvalid));
        Assert.IsFalse(pageSizeInvalid);
    }

    [TestMethod]
    public void Envelope_sem_recorte_tem_page_1_e_pageSize_igual_ao_total()
    {
        var response = PagedResponse<int>.From(null, [1, 2, 3], 3);

        Assert.AreEqual(1, response.Page);
        Assert.AreEqual(3, response.PageSize);
        Assert.AreEqual(3, response.Total);
    }
}
