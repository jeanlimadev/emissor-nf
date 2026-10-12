using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Tests;
using Emissor.Modules.Nfe.Infrastructure.Webhooks;
using Emissor.Modules.Nfe.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Emissor.Api.Tests;

/// <summary>
/// Webhook de ponta a ponta: um receptor HTTP de verdade (Kestrel em porta
/// livre) recebe os POSTs do worker e confere a assinatura.
/// </summary>
[TestClass]
public sealed class WebhookApiTests
{
    private static EmissorApiFactory _factory = null!;
    private static WebApplication _receptor = null!;
    private static string _urlBase = null!;

    /// <summary>Requisicoes recebidas, por caminho.</summary>
    private static readonly ConcurrentDictionary<string, ConcurrentQueue<(string Corpo, IReadOnlyDictionary<string, string> Headers)>> Recebidas = new();

    /// <summary>Caminhos que devem responder erro (e quantas vezes ainda).</summary>
    private static readonly ConcurrentDictionary<string, int> FalhasRestantes = new();

    [ClassInitialize]
    public static async Task Up(TestContext _)
    {
        _factory = new EmissorApiFactory();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        _receptor = builder.Build();
        _receptor.MapPost("/{caminho}", async (string caminho, HttpRequest request) =>
        {
            using var leitor = new StreamReader(request.Body, Encoding.UTF8);
            var corpo = await leitor.ReadToEndAsync();
            var headers = request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
            Recebidas.GetOrAdd(caminho, _ => new()).Enqueue((corpo, headers));

            if (FalhasRestantes.TryGetValue(caminho, out var restantes) && restantes > 0)
            {
                FalhasRestantes[caminho] = restantes - 1;
                return Results.StatusCode(500);
            }

            return Results.NoContent();
        });
        await _receptor.StartAsync();
        _urlBase = _receptor.Urls.First();
    }

    [ClassCleanup]
    public static async Task Down()
    {
        await _receptor.StopAsync();
        await _receptor.DisposeAsync();
        _factory.Dispose();
    }

    [TestMethod]
    public async Task Configura_testa_e_recebe_evento_assinado()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant webhook teste");
        using var client = _factory.CreateClient().AsTenant(apiKey);

        var configuracao = await client.PutAsJsonAsync("/api/v1/webhook", new WebhookRequest(_urlBase + "/teste", null));
        Assert.AreEqual(HttpStatusCode.OK, configuracao.StatusCode, await configuracao.Content.ReadAsStringAsync());
        var configurado = (await configuracao.Content.ReadFromJsonAsync<WebhookConfiguradoResponse>())!;
        Assert.AreEqual(43, configurado.Segredo.Length, "segredo gerado pelo servico");

        var consulta = await client.GetFromJsonAsync<WebhookResponse>("/api/v1/webhook");
        Assert.AreEqual(configurado.Url, consulta!.Url);
        Assert.IsFalse((await client.GetStringAsync("/api/v1/webhook")).Contains(configurado.Segredo), "o segredo nao volta na consulta");

        var teste = await client.PostAsync("/api/v1/webhook/testar", null);
        Assert.AreEqual(HttpStatusCode.Accepted, teste.StatusCode, await teste.Content.ReadAsStringAsync());
        var entrega = (await teste.Content.ReadFromJsonAsync<WebhookEntregaResponse>())!;

        var recebida = await AguardarRecebimentoAsync("teste");
        Assert.AreEqual("webhook.teste", recebida.Headers[AssinaturaWebhook.HeaderEvento]);
        Assert.AreEqual(entrega.Id.ToString(), recebida.Headers[AssinaturaWebhook.HeaderEntrega]);
        Assert.IsTrue(AssinaturaWebhook.Conferir(Encoding.UTF8.GetBytes(configurado.Segredo), Encoding.UTF8.GetBytes(recebida.Corpo), recebida.Headers[AssinaturaWebhook.HeaderAssinatura]), "assinatura HMAC confere com o segredo devolvido");
        var payload = JsonDocument.Parse(recebida.Corpo).RootElement;
        Assert.AreEqual("webhook.teste", payload.GetProperty("evento").GetString());
        Assert.AreEqual(entrega.Id, payload.GetProperty("entregaId").GetGuid());

        var entregas = await AguardarEntregueAsync(client, entrega.Id);
        Assert.AreEqual(WebhookEntregaStatus.Entregue, entregas.Status);
        Assert.AreEqual(204, entregas.UltimoHttpStatus);
    }

    [TestMethod]
    public async Task Nota_autorizada_dispara_nfe_autorizada_com_a_nota_no_corpo()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant webhook nota");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.EmitenteProntoAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido), $"X:{CertificadoDeTeste.CpfValido}", 1, 1);
        var segredo = "segredo-escolhido-pelo-cliente-123";
        await client.PutAsJsonAsync("/api/v1/webhook", new WebhookRequest(_urlBase + "/nota", segredo));

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/emitentes/{emitente.Id}/nfe?aguardar=30") { Content = JsonContent.Create(PedidosDeTeste.PedidoRegimeNormal()) };
        request.Headers.Add("Idempotency-Key", "wh-1");
        var nota = (await (await client.SendAsync(request)).Content.ReadFromJsonAsync<NfeResponse>())!;
        Assert.AreEqual(NfeStatus.Autorizada, nota.Status, nota.XMotivo);

        var recebida = await AguardarRecebimentoAsync("nota");
        Assert.AreEqual("nfe.autorizada", recebida.Headers[AssinaturaWebhook.HeaderEvento]);
        Assert.IsTrue(AssinaturaWebhook.Conferir(Encoding.UTF8.GetBytes(segredo), Encoding.UTF8.GetBytes(recebida.Corpo), recebida.Headers[AssinaturaWebhook.HeaderAssinatura]));
        var payload = JsonSerializer.Deserialize<WebhookEventoPayload>(recebida.Corpo, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.AreEqual(nota.Id, payload.Nota!.Id);
        Assert.AreEqual(NfeStatus.Autorizada, payload.Nota.Status);
        Assert.AreEqual(nota.Protocolo, payload.Nota.Protocolo);

        var lista = await client.GetFromJsonAsync<Emissor.BuildingBlocks.PagedResponse<WebhookEntregaResponse>>($"/api/v1/webhook/entregas?nfeId={nota.Id}");
        Assert.AreEqual(1, lista!.Total);
    }

    [TestMethod]
    public async Task Receptor_com_erro_recebe_retentativa_ate_dar_certo()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant webhook retry");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        await client.PutAsJsonAsync("/api/v1/webhook", new WebhookRequest(_urlBase + "/instavel", null));
        FalhasRestantes["instavel"] = 2;

        var entrega = (await (await client.PostAsync("/api/v1/webhook/testar", null)).Content.ReadFromJsonAsync<WebhookEntregaResponse>())!;

        var final = await AguardarEntregueAsync(client, entrega.Id);
        Assert.AreEqual(WebhookEntregaStatus.Entregue, final.Status);
        Assert.AreEqual(3, final.Tentativas, "duas falhas e um sucesso");
        Assert.IsTrue(Recebidas["instavel"].Count >= 3);
    }

    [TestMethod]
    public async Task Certificado_vencendo_gera_evento_no_webhook()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant webhook cert");
        using var client = _factory.CreateClient().AsTenant(apiKey);
        var emitente = await client.CriarEmitenteAsync(ApiClientExtensions.EmitentePf(CertificadoDeTeste.CpfValido));
        using var form = ApiClientExtensions.CertificadoForm(CertificadoDeTeste.PfxComCn($"X:{CertificadoDeTeste.CpfValido}", DateTimeOffset.UtcNow.AddDays(7)), CertificadoDeTeste.Senha);
        Assert.AreEqual(HttpStatusCode.OK, (await client.PutAsync($"/api/v1/emitentes/{emitente.Id}/certificado", form)).StatusCode);
        await client.PutAsJsonAsync("/api/v1/webhook", new WebhookRequest(_urlBase + "/cert", null));

        var recebida = await AguardarRecebimentoAsync("cert");

        Assert.AreEqual("emitente.certificado_vencendo", recebida.Headers[AssinaturaWebhook.HeaderEvento]);
        var payload = JsonSerializer.Deserialize<WebhookEventoPayload>(recebida.Corpo, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.AreEqual(emitente.Id, payload.Certificado!.EmitenteId);
        Assert.IsTrue(payload.Certificado.DiasRestantes is >= 5 and <= 7, $"dias restantes: {payload.Certificado.DiasRestantes}");
    }

    [TestMethod]
    public async Task Url_http_fora_do_localhost_e_recusada_e_sem_webhook_e_404()
    {
        var (_, apiKey) = await _factory.NovoTenantComChaveAsync("Tenant webhook url");
        using var client = _factory.CreateClient().AsTenant(apiKey);

        var recusada = await client.PutAsJsonAsync("/api/v1/webhook", new WebhookRequest("http://erp.exemplo.com.br/hook", null));
        Assert.AreEqual(HttpStatusCode.BadRequest, recusada.StatusCode);
        StringAssert.Contains(await recusada.Content.ReadAsStringAsync(), "webhook.url_invalida");

        Assert.AreEqual(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/webhook")).StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, (await client.PostAsync("/api/v1/webhook/testar", null)).StatusCode);
    }

    private static async Task<(string Corpo, IReadOnlyDictionary<string, string> Headers)> AguardarRecebimentoAsync(string caminho)
    {
        var limite = DateTimeOffset.UtcNow.AddSeconds(30);

        while (DateTimeOffset.UtcNow < limite)
        {
            if (Recebidas.TryGetValue(caminho, out var fila) && fila.TryDequeue(out var recebida))
                return recebida;

            await Task.Delay(200);
        }

        Assert.Fail($"nenhuma requisição chegou em /{caminho} em 30 s");
        return default;
    }

    private static async Task<WebhookEntregaResponse> AguardarEntregueAsync(HttpClient client, Guid entregaId)
    {
        var limite = DateTimeOffset.UtcNow.AddSeconds(30);
        WebhookEntregaResponse? ultima = null;

        while (DateTimeOffset.UtcNow < limite)
        {
            var lista = (await client.GetFromJsonAsync<Emissor.BuildingBlocks.PagedResponse<WebhookEntregaResponse>>("/api/v1/webhook/entregas"))!;
            ultima = lista.Items.FirstOrDefault(e => e.Id == entregaId);

            if (ultima is { Status: not WebhookEntregaStatus.Pendente })
                return ultima;

            await Task.Delay(250);
        }

        return ultima ?? throw new AssertFailedException("entrega não encontrada");
    }
}
