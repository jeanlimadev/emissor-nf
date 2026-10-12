using System.Net;
using System.Net.Http.Json;
using Emissor.Modules.Tenancy.Application.ApiKeys;
using Emissor.Modules.Tenancy.Presentation.Me;

namespace Emissor.Api.Tests;

[TestClass]
public sealed class AutenticacaoTests
{
    private static EmissorApiFactory _factory = null!;

    [ClassInitialize]
    public static void Up(TestContext _) => _factory = new EmissorApiFactory();

    [ClassCleanup]
    public static void Down() => _factory.Dispose();

    [TestMethod]
    public async Task Sem_chave_e_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/v1/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Chave_invalida_e_401()
    {
        using var client = _factory.CreateClient().AsTenant("emk_abcdefgh_naoexiste");

        var response = await client.GetAsync("/api/v1/me");

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Chave_valida_identifica_o_tenant()
    {
        var (tenant, apiKey) = await _factory.NovoTenantComChaveAsync("ERP de teste");
        using var client = _factory.CreateClient().AsTenant(apiKey);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/v1/me");

        Assert.AreEqual(tenant.Id, me!.TenantId);
        Assert.AreEqual("ERP de teste", me.Nome);
    }

    [TestMethod]
    public async Task Chave_revogada_deixa_de_abrir()
    {
        var (tenant, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant revogado");
        using var admin = _factory.CreateClient().AsAdmin();
        using var client = _factory.CreateClient().AsTenant(apiKey);

        Assert.AreEqual(HttpStatusCode.OK, (await client.GetAsync("/api/v1/me")).StatusCode);

        var chaves = await admin.GetFromJsonAsync<List<ApiKeyResponse>>($"/api/v1/admin/tenants/{tenant.Id}/api-keys");
        var alvo = chaves!.Single(c => c.Prefixo == apiKey.Split('_')[1]);

        var revogada = await admin.DeleteAsync($"/api/v1/admin/tenants/{tenant.Id}/api-keys/{alvo.Id}");
        Assert.AreEqual(HttpStatusCode.OK, revogada.StatusCode, await revogada.Content.ReadAsStringAsync());

        Assert.AreEqual(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
    }

    [TestMethod]
    public async Task Admin_sem_token_e_401()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/admin/tenants", new { nome = "x" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [TestMethod]
    public async Task Chave_de_tenant_nao_abre_a_administracao()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant curioso");
        using var client = _factory.CreateClient().AsTenant(apiKey);

        var response = await client.PostAsJsonAsync("/api/v1/admin/tenants", new { nome = "x" });

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
