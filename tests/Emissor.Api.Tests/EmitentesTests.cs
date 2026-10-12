using System.Net;
using System.Net.Http.Json;
using Emissor.Contracts.Emitentes;
using Emissor.Modules.Emitentes.Tests;

namespace Emissor.Api.Tests;

[TestClass]
public sealed class EmitentesTests
{
    private static EmissorApiFactory _factory = null!;

    [ClassInitialize]
    public static void Up(TestContext _) => _factory = new EmissorApiFactory();

    [ClassCleanup]
    public static void Down() => _factory.Dispose();

    [TestMethod]
    public async Task Cadastra_consulta_e_lista_emitente_pf()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant A");
        using var client = _factory.CreateClient().AsTenant(apiKey);

        var criado = await client.CriarEmitenteAsync(ApiClientExtensions.EmitentePf("123.456.789-09"));

        Assert.AreEqual("12345678909", criado.Documento);
        Assert.IsFalse(criado.PossuiCertificado);

        var consultado = await client.GetFromJsonAsync<EmitenteResponse>($"/api/v1/emitentes/{criado.Id}");
        Assert.AreEqual(criado.Id, consultado!.Id);

        var lista = await client.GetFromJsonAsync<Emissor.BuildingBlocks.PagedResponse<EmitenteResponse>>("/api/v1/emitentes?page=1&pageSize=10");
        Assert.AreEqual(1, lista!.Total);
    }

    [TestMethod]
    public async Task Documento_duplicado_no_tenant_e_409()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant dup");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        await client.CriarEmitenteAsync(ApiClientExtensions.EmitentePf());

        var response = await client.PostAsJsonAsync("/api/v1/emitentes", ApiClientExtensions.EmitentePf());

        Assert.AreEqual(HttpStatusCode.Conflict, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "emitente.documento_duplicado");
    }

    [TestMethod]
    public async Task Cpf_invalido_e_400_com_codigo()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant cpf");
        using var client = _factory.CreateClient().AsTenant(apiKey);

        var response = await client.PostAsJsonAsync("/api/v1/emitentes", ApiClientExtensions.EmitentePf("111.111.111-11"));

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "emitente.cpf_invalido");
    }

    [TestMethod]
    public async Task Tenant_nao_enxerga_emitente_de_outro_tenant()
    {
        var (_, chaveA) = await _factory.NovoTenantComChaveAsync("Tenant A iso");
        var (_, chaveB) = await _factory.NovoTenantComChaveAsync("Tenant B iso");
        using var clientA = _factory.CreateClient().AsTenant(chaveA);
        using var clientB = _factory.CreateClient().AsTenant(chaveB);

        var emitenteDeA = await clientA.CriarEmitenteAsync(ApiClientExtensions.EmitentePf());

        Assert.AreEqual(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/v1/emitentes/{emitenteDeA.Id}")).StatusCode);

        var listaB = await clientB.GetFromJsonAsync<Emissor.BuildingBlocks.PagedResponse<EmitenteResponse>>("/api/v1/emitentes");
        Assert.AreEqual(0, listaB!.Total);

        // O mesmo CPF pode existir em outro tenant: a unicidade e por tenant.
        await clientB.CriarEmitenteAsync(ApiClientExtensions.EmitentePf());
    }

    [TestMethod]
    public async Task Envia_certificado_do_titular_e_le_os_metadados()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant cert");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.CriarEmitenteAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido));
        var pfx = CertificadoDeTeste.PfxComCn($"PRODUTOR RURAL TESTE:{CertificadoDeTeste.CpfValido}");

        using var form = ApiClientExtensions.CertificadoForm(pfx, CertificadoDeTeste.Senha);
        var response = await client.PutAsync($"/api/v1/emitentes/{emitente.Id}/certificado", form);

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode, await response.Content.ReadAsStringAsync());
        var certificado = (await response.Content.ReadFromJsonAsync<CertificadoResponse>())!;
        Assert.AreEqual(CertificadoDeTeste.CpfValido, certificado.Documento);
        Assert.AreEqual("PRODUTOR RURAL TESTE", certificado.Titular);

        var corpo = await response.Content.ReadAsStringAsync();
        Assert.IsFalse(corpo.Contains(CertificadoDeTeste.Senha), "a senha nunca volta na resposta");

        var atualizado = await client.GetFromJsonAsync<EmitenteResponse>($"/api/v1/emitentes/{emitente.Id}");
        Assert.IsTrue(atualizado!.PossuiCertificado);
        Assert.IsNotNull(atualizado.CertificadoValidoAte);

        // Troca por outro: o anterior sai, o novo entra.
        using var form2 = ApiClientExtensions.CertificadoForm(
            CertificadoDeTeste.PfxComCn($"OUTRO NOME:{CertificadoDeTeste.CpfValido}"), CertificadoDeTeste.Senha);
        var troca = await client.PutAsync($"/api/v1/emitentes/{emitente.Id}/certificado", form2);
        Assert.AreEqual(HttpStatusCode.OK, troca.StatusCode, await troca.Content.ReadAsStringAsync());
        var meta = await client.GetFromJsonAsync<CertificadoResponse>($"/api/v1/emitentes/{emitente.Id}/certificado");
        Assert.AreEqual("OUTRO NOME", meta!.Titular);

        Assert.AreEqual(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/emitentes/{emitente.Id}/certificado")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/emitentes/{emitente.Id}/certificado")).StatusCode);
    }

    [TestMethod]
    public async Task Certificado_de_outro_titular_e_400()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant cert errado");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.CriarEmitenteAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido));
        var pfx = CertificadoDeTeste.PfxComCn("EMPRESA:11.222.333/0001-81");

        using var form = ApiClientExtensions.CertificadoForm(pfx, CertificadoDeTeste.Senha);
        var response = await client.PutAsync($"/api/v1/emitentes/{emitente.Id}/certificado", form);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "certificado.outro_titular");
    }

    [TestMethod]
    public async Task Senha_errada_do_certificado_e_400()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant senha");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.CriarEmitenteAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido));
        var pfx = CertificadoDeTeste.PfxComCn($"X:{CertificadoDeTeste.CpfValido}");

        using var form = ApiClientExtensions.CertificadoForm(pfx, "errada");
        var response = await client.PutAsync($"/api/v1/emitentes/{emitente.Id}/certificado", form);

        Assert.AreEqual(HttpStatusCode.BadRequest, response.StatusCode);
        StringAssert.Contains(await response.Content.ReadAsStringAsync(), "certificado.ilegivel");
    }

    [TestMethod]
    public async Task Series_sao_criadas_listadas_e_ajustadas()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant series");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.CriarEmitenteAsync(ApiClientExtensions.EmitentePf());

        var criada = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/series", new SerieRequest(1, 100));
        Assert.AreEqual(HttpStatusCode.Created, criada.StatusCode, await criada.Content.ReadAsStringAsync());
        var serie = (await criada.Content.ReadFromJsonAsync<SerieResponse>())!;
        Assert.AreEqual(55, serie.Modelo);

        var duplicada = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/series", new SerieRequest(1, 1));
        Assert.AreEqual(HttpStatusCode.Conflict, duplicada.StatusCode);

        var retrocesso = await client.PutAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/series/{serie.Id}", new { proximoNumero = 50 });
        Assert.AreEqual(HttpStatusCode.BadRequest, retrocesso.StatusCode);

        var avanco = await client.PutAsJsonAsync($"/api/v1/emitentes/{emitente.Id}/series/{serie.Id}", new { proximoNumero = 200, ativa = false });
        Assert.AreEqual(HttpStatusCode.OK, avanco.StatusCode, await avanco.Content.ReadAsStringAsync());
        var ajustada = (await avanco.Content.ReadFromJsonAsync<SerieResponse>())!;
        Assert.AreEqual(200L, ajustada.ProximoNumero);
        Assert.IsFalse(ajustada.Ativa);

        var lista = await client.GetFromJsonAsync<List<SerieResponse>>($"/api/v1/emitentes/{emitente.Id}/series");
        Assert.AreEqual(1, lista!.Count);
    }

    [TestMethod]
    public async Task Health_responde_saudavel_com_o_banco_de_pe()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
