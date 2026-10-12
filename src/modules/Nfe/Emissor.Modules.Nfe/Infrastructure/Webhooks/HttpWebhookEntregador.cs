using System.Globalization;
using System.Text;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using Microsoft.Extensions.Logging;

namespace Emissor.Modules.Nfe.Infrastructure.Webhooks;

/// <summary>POST JSON assinado, com timeout curto: webhook lento nao pode segurar a fila.</summary>
public sealed class HttpWebhookEntregador(IHttpClientFactory httpClients, ILogger<HttpWebhookEntregador> logger) : IWebhookEntregador
{
    public const string NomeCliente = "webhook";

    public async Task<ResultadoEntrega> EntregarAsync(Uri url, byte[] segredo, WebhookEntrega entrega, CancellationToken cancellationToken)
    {
        var corpo = Encoding.UTF8.GetBytes(entrega.Payload);

        using var conteudo = new ByteArrayContent(corpo);
        conteudo.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };

        using var requisicao = new HttpRequestMessage(HttpMethod.Post, url) { Content = conteudo };
        requisicao.Headers.TryAddWithoutValidation(AssinaturaWebhook.HeaderEvento, entrega.Evento);
        requisicao.Headers.TryAddWithoutValidation(AssinaturaWebhook.HeaderEntrega, entrega.Id.ToString());
        requisicao.Headers.TryAddWithoutValidation(AssinaturaWebhook.HeaderTimestamp, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        requisicao.Headers.TryAddWithoutValidation(AssinaturaWebhook.HeaderAssinatura, AssinaturaWebhook.Assinar(segredo, corpo));
        requisicao.Headers.TryAddWithoutValidation("User-Agent", "emissor-nf-webhook/1.0");

        try
        {
            using var resposta = await httpClients.CreateClient(NomeCliente).SendAsync(requisicao, cancellationToken);
            var status = (int)resposta.StatusCode;

            if (resposta.IsSuccessStatusCode)
                return new ResultadoEntrega(true, status, null);

            logger.LogWarning("Webhook {Url} respondeu HTTP {Status} para a entrega {EntregaId}.", url, status, entrega.Id);
            return new ResultadoEntrega(false, status, $"HTTP {status}");
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Falha de rede ao entregar o webhook {Url} (entrega {EntregaId}).", url, entrega.Id);
            return new ResultadoEntrega(false, null, "Falha de rede: " + ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timeout ao entregar o webhook {Url} (entrega {EntregaId}).", url, entrega.Id);
            return new ResultadoEntrega(false, null, "Timeout.");
        }
    }
}
