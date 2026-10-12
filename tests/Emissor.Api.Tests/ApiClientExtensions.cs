using System.Net.Http.Headers;
using System.Net.Http.Json;
using Emissor.Contracts.Emitentes;
using Emissor.Modules.Emitentes.Tests;
using Emissor.Modules.Tenancy.Application.ApiKeys;
using Emissor.Modules.Tenancy.Application.Tenants;
using Emissor.Modules.Tenancy.Infrastructure.Auth;

namespace Emissor.Api.Tests;

internal static class ApiClientExtensions
{
    internal static HttpClient AsAdmin(this HttpClient client)
    {
        client.DefaultRequestHeaders.Remove(AdminTokenAuthenticationHandler.HeaderName);
        client.DefaultRequestHeaders.Add(AdminTokenAuthenticationHandler.HeaderName, EmissorApiFactory.AdminToken);
        return client;
    }

    internal static HttpClient AsTenant(this HttpClient client, string apiKey)
    {
        client.DefaultRequestHeaders.Remove(ApiKeyAuthenticationHandler.HeaderName);
        client.DefaultRequestHeaders.Add(ApiKeyAuthenticationHandler.HeaderName, apiKey);
        return client;
    }

    /// <summary>Cria um tenant e uma chave pelo caminho de administracao; devolve a chave em texto.</summary>
    internal static async Task<(TenantResponse Tenant, string ApiKey)> NovoTenantComChaveAsync(this EmissorApiFactory factory, string nome)
    {
        using var admin = factory.CreateClient().AsAdmin();

        var tenantResponse = await admin.PostAsJsonAsync("/api/v1/admin/tenants", new { nome });
        tenantResponse.EnsureSuccessStatusCode();
        var tenant = (await tenantResponse.Content.ReadFromJsonAsync<TenantResponse>())!;

        var keyResponse = await admin.PostAsJsonAsync($"/api/v1/admin/tenants/{tenant.Id}/api-keys", new { nome = "chave de teste" });
        keyResponse.EnsureSuccessStatusCode();
        var key = (await keyResponse.Content.ReadFromJsonAsync<ApiKeyCreatedResponse>())!;

        return (tenant, key.Chave);
    }

    internal static EmitenteRequest EmitentePf(string cpf = "12345678909") => new(
        TipoEmitente.PessoaFisica,
        cpf,
        "12345678",
        "Produtor Rural Teste",
        null,
        RegimeTributario.RegimeNormal,
        AmbienteSefaz.Homologacao,
        new EnderecoDto("Estrada do Produtor", "KM 12", null, "Zona Rural", "3304557", "Rio de Janeiro", "RJ", "20000000", null),
        null);

    internal static async Task<EmitenteResponse> CriarEmitenteAsync(this HttpClient client, EmitenteRequest request)
    {
        var response = await client.PostAsJsonAsync("/api/v1/emitentes", request);
        Assert.AreEqual(System.Net.HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<EmitenteResponse>())!;
    }

    internal static async Task<SerieResponse> CriarSerieAsync(this HttpClient client, Guid emitenteId, int numero, long proximoNumero)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/emitentes/{emitenteId}/series", new SerieRequest(numero, proximoNumero));
        Assert.AreEqual(System.Net.HttpStatusCode.Created, response.StatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<SerieResponse>())!;
    }

    /// <summary>Emitente cadastrado, com certificado autoassinado do titular e uma serie: pronto para a previa.</summary>
    internal static async Task<EmitenteResponse> EmitenteProntoAsync(this HttpClient client, EmitenteRequest request, string commonNameDoCertificado, int serie, long proximoNumero)
    {
        var emitente = await client.CriarEmitenteAsync(request);

        using var form = CertificadoForm(CertificadoDeTeste.PfxComCn(commonNameDoCertificado), CertificadoDeTeste.Senha);
        var certificado = await client.PutAsync($"/api/v1/emitentes/{emitente.Id}/certificado", form);
        Assert.AreEqual(System.Net.HttpStatusCode.OK, certificado.StatusCode, await certificado.Content.ReadAsStringAsync());

        await client.CriarSerieAsync(emitente.Id, serie, proximoNumero);

        return emitente;
    }

    internal static MultipartFormDataContent CertificadoForm(byte[] pfx, string senha)
    {
        var form = new MultipartFormDataContent();
        var arquivo = new ByteArrayContent(pfx);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/x-pkcs12");
        form.Add(arquivo, "arquivo", "certificado.pfx");
        form.Add(new StringContent(senha), "senha");
        return form;
    }
}
