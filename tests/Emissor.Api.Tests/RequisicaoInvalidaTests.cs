using System.Net;
using System.Text;
using System.Text.Json;
using Emissor.Api.Infrastructure;

namespace Emissor.Api.Tests;

[TestClass]
public sealed class RequisicaoInvalidaTests
{
    private static EmissorApiFactory _factory = null!;

    [ClassInitialize]
    public static void Up(TestContext _) => _factory = new EmissorApiFactory();

    [ClassCleanup]
    public static void Down() => _factory.Dispose();

    [TestMethod]
    public async Task Corpo_que_nao_e_json_devolve_400_com_codigo_e_nao_500()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant json ruim");
        using var client = _factory.CreateClient().AsTenant(apiKey);

        // O erro classico: "-d pedido.json" sem o arroba manda o nome do arquivo como corpo.
        using var corpo = new StringContent("pedido.json", Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/v1/emitentes/{Guid.NewGuid()}/nfe/previa", corpo);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var problema = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.AreEqual(RequisicaoInvalidaExceptionHandler.Code, problema.GetProperty("code").GetString());
        StringAssert.Contains(problema.GetProperty("detail").GetString(), "'p'");
        StringAssert.Contains(problema.GetProperty("detail").GetString(), "não é um JSON válido");
    }

    [TestMethod]
    public async Task Aspa_sem_escape_dentro_do_texto_explica_o_que_fazer()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant aspa");
        using var client = _factory.CreateClient().AsTenant(apiKey);

        using var corpo = new StringContent("""{ "justificativa": ""Nota de teste", cancelada." }""", Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/v1/nfe/{Guid.NewGuid()}/cancelamento", corpo);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        var detalhe = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString()!;
        StringAssert.Contains(detalhe, "aspas");
        StringAssert.Contains(detalhe, "\\\"");
    }

    [TestMethod]
    public async Task Campo_com_tipo_errado_devolve_400()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant tipo errado");
        using var client = _factory.CreateClient().AsTenant(apiKey);

        using var corpo = new StringContent("""{"numero": "um", "proximoNumero": 1}""", Encoding.UTF8, "application/json");
        var response = await client.PostAsync($"/api/v1/emitentes/{Guid.NewGuid()}/series", corpo);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), RequisicaoInvalidaExceptionHandler.Code);
    }
}
