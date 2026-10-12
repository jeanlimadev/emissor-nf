using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Emissor.Contracts.Emitentes;
using Emissor.Contracts.Nfe;

namespace Emissor.Contracts;

/// <summary>
/// Cliente HTTP tipado do emissor, para o ERP e os outros sistemas clientes.
/// So depende da BCL. Cada metodo devolve <see cref="Resposta{T}"/>: sucesso
/// com o valor, ou o <see cref="Problema"/> (ProblemDetails) que a API devolveu,
/// com o <c>code</c> estavel para tratar por codigo e nao por texto.
/// Configure o <see cref="HttpClient"/> com <c>BaseAddress</c>; a chave vai no header.
/// </summary>
public sealed class EmissorClient
{
    public const string HeaderChave = "X-Api-Key";
    public const string HeaderIdempotencia = "Idempotency-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http;

    public EmissorClient(HttpClient http, string apiKey)
    {
        _http = http;
        _http.DefaultRequestHeaders.Remove(HeaderChave);
        _http.DefaultRequestHeaders.Add(HeaderChave, apiKey);
    }

    // ----- emitentes --------------------------------------------------------------------------------

    public Task<Resposta<EmitenteResponse>> CriarEmitenteAsync(EmitenteRequest request, CancellationToken ct = default) =>
        EnviarAsync<EmitenteResponse>(HttpMethod.Post, "api/v1/emitentes", request, ct);

    public Task<Resposta<EmitenteResponse>> ObterEmitenteAsync(Guid id, CancellationToken ct = default) =>
        EnviarAsync<EmitenteResponse>(HttpMethod.Get, $"api/v1/emitentes/{id}", null, ct);

    public Task<Resposta<EmitenteResponse>> AtualizarEmitenteAsync(Guid id, EmitenteRequest request, CancellationToken ct = default) =>
        EnviarAsync<EmitenteResponse>(HttpMethod.Put, $"api/v1/emitentes/{id}", request, ct);

    public async Task<Resposta<CertificadoResponse>> EnviarCertificadoAsync(Guid emitenteId, byte[] pfx, string senha, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var arquivo = new ByteArrayContent(pfx);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue("application/x-pkcs12");
        form.Add(arquivo, "arquivo", "certificado.pfx");
        form.Add(new StringContent(senha), "senha");

        using var response = await _http.PutAsync($"api/v1/emitentes/{emitenteId}/certificado", form, ct);
        return await LerAsync<CertificadoResponse>(response, ct);
    }

    public Task<Resposta<SerieResponse>> CriarSerieAsync(Guid emitenteId, SerieRequest request, CancellationToken ct = default) =>
        EnviarAsync<SerieResponse>(HttpMethod.Post, $"api/v1/emitentes/{emitenteId}/series", request, ct);

    public Task<Resposta<List<SerieResponse>>> ListarSeriesAsync(Guid emitenteId, CancellationToken ct = default) =>
        EnviarAsync<List<SerieResponse>>(HttpMethod.Get, $"api/v1/emitentes/{emitenteId}/series", null, ct);

    public Task<Resposta<StatusServicoSefazResponse>> StatusSefazAsync(Guid emitenteId, CancellationToken ct = default) =>
        EnviarAsync<StatusServicoSefazResponse>(HttpMethod.Get, $"api/v1/emitentes/{emitenteId}/sefaz/status", null, ct);

    public Task<Resposta<EmitenteResponse>> AtivarContingenciaAsync(Guid emitenteId, string justificativa, CancellationToken ct = default) =>
        EnviarAsync<EmitenteResponse>(HttpMethod.Post, $"api/v1/emitentes/{emitenteId}/contingencia", new ContingenciaRequest(justificativa), ct);

    public Task<Resposta<EmitenteResponse>> DesativarContingenciaAsync(Guid emitenteId, CancellationToken ct = default) =>
        EnviarAsync<EmitenteResponse>(HttpMethod.Delete, $"api/v1/emitentes/{emitenteId}/contingencia", null, ct);

    // ----- notas ------------------------------------------------------------------------------------

    /// <param name="idempotencyKey">Identificador unico do pedido no seu sistema (ex.: o id do pedido de venda). Repetir devolve a mesma nota.</param>
    /// <param name="aguardarSegundos">Ate 60: espera o resultado da SEFAZ na propria chamada.</param>
    public Task<Resposta<NfeResponse>> EmitirAsync(Guid emitenteId, EmissaoNfeRequest pedido, string idempotencyKey, int? aguardarSegundos = null, CancellationToken ct = default)
    {
        var url = $"api/v1/emitentes/{emitenteId}/nfe" + (aguardarSegundos is null ? string.Empty : $"?aguardar={aguardarSegundos}");
        return EnviarAsync<NfeResponse>(HttpMethod.Post, url, pedido, ct, [(HeaderIdempotencia, idempotencyKey)]);
    }

    public Task<Resposta<NfePreviaResponse>> PreviaAsync(Guid emitenteId, EmissaoNfeRequest pedido, CancellationToken ct = default) =>
        EnviarAsync<NfePreviaResponse>(HttpMethod.Post, $"api/v1/emitentes/{emitenteId}/nfe/previa", pedido, ct);

    public Task<Resposta<NfeResponse>> ObterNotaAsync(Guid nfeId, CancellationToken ct = default) =>
        EnviarAsync<NfeResponse>(HttpMethod.Get, $"api/v1/nfe/{nfeId}", null, ct);

    public Task<Resposta<string>> ObterXmlAsync(Guid nfeId, CancellationToken ct = default) =>
        TextoAsync($"api/v1/nfe/{nfeId}/xml", ct);

    public Task<Resposta<byte[]>> ObterDanfeAsync(Guid nfeId, CancellationToken ct = default) =>
        BytesAsync($"api/v1/nfe/{nfeId}/danfe", ct);

    public Task<Resposta<NfeResponse>> ReenviarAsync(Guid nfeId, EmissaoNfeRequest? pedidoCorrigido, CancellationToken ct = default) =>
        EnviarAsync<NfeResponse>(HttpMethod.Post, $"api/v1/nfe/{nfeId}/reenviar", pedidoCorrigido, ct);

    public Task<Resposta<EventoNfeResponse>> CancelarAsync(Guid nfeId, string justificativa, CancellationToken ct = default) =>
        EnviarAsync<EventoNfeResponse>(HttpMethod.Post, $"api/v1/nfe/{nfeId}/cancelamento", new CancelamentoRequest(justificativa), ct);

    public Task<Resposta<EventoNfeResponse>> CartaCorrecaoAsync(Guid nfeId, string correcao, CancellationToken ct = default) =>
        EnviarAsync<EventoNfeResponse>(HttpMethod.Post, $"api/v1/nfe/{nfeId}/carta-correcao", new CartaCorrecaoRequest(correcao), ct);

    public Task<Resposta<List<EventoNfeResponse>>> ListarEventosAsync(Guid nfeId, CancellationToken ct = default) =>
        EnviarAsync<List<EventoNfeResponse>>(HttpMethod.Get, $"api/v1/nfe/{nfeId}/eventos", null, ct);

    public Task<Resposta<InutilizacaoResponse>> InutilizarAsync(Guid emitenteId, InutilizacaoRequest request, CancellationToken ct = default) =>
        EnviarAsync<InutilizacaoResponse>(HttpMethod.Post, $"api/v1/emitentes/{emitenteId}/inutilizacoes", request, ct);

    // ----- webhook ----------------------------------------------------------------------------------

    public Task<Resposta<WebhookConfiguradoResponse>> ConfigurarWebhookAsync(WebhookRequest request, CancellationToken ct = default) =>
        EnviarAsync<WebhookConfiguradoResponse>(HttpMethod.Put, "api/v1/webhook", request, ct);

    // ----- base -------------------------------------------------------------------------------------

    private async Task<Resposta<T>> EnviarAsync<T>(HttpMethod metodo, string url, object? corpo, CancellationToken ct, IEnumerable<(string, string)>? headers = null)
    {
        using var request = new HttpRequestMessage(metodo, url);

        if (corpo is not null)
            request.Content = JsonContent.Create(corpo, options: Json);

        foreach (var (nome, valor) in headers ?? [])
            request.Headers.TryAddWithoutValidation(nome, valor);

        using var response = await _http.SendAsync(request, ct);
        return await LerAsync<T>(response, ct);
    }

    private async Task<Resposta<string>> TextoAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);

        return response.IsSuccessStatusCode
            ? Resposta<string>.Ok(response.StatusCode, await response.Content.ReadAsStringAsync(ct))
            : Resposta<string>.Erro(response.StatusCode, await LerProblemaAsync(response, ct));
    }

    private async Task<Resposta<byte[]>> BytesAsync(string url, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, ct);

        return response.IsSuccessStatusCode
            ? Resposta<byte[]>.Ok(response.StatusCode, await response.Content.ReadAsByteArrayAsync(ct))
            : Resposta<byte[]>.Erro(response.StatusCode, await LerProblemaAsync(response, ct));
    }

    private static async Task<Resposta<T>> LerAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
            return Resposta<T>.Erro(response.StatusCode, await LerProblemaAsync(response, ct));

        if (response.StatusCode == HttpStatusCode.NoContent)
            return Resposta<T>.Ok(response.StatusCode, default!);

        var valor = await response.Content.ReadFromJsonAsync<T>(Json, ct);

        return Resposta<T>.Ok(response.StatusCode, valor!);
    }

    private static async Task<Problema> LerProblemaAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problema = await response.Content.ReadFromJsonAsync<Problema>(Json, ct);

            if (problema is not null)
                return problema with { Status = problema.Status ?? (int)response.StatusCode };
        }
        catch (JsonException)
        {
            // Resposta sem ProblemDetails (proxy, gateway): cai no generico abaixo.
        }

        return new Problema(null, "Resposta sem detalhes.", null, (int)response.StatusCode, null, null);
    }
}

/// <summary>ProblemDetails da API: <c>code</c> estavel e, nas validacoes, <c>errors</c> por campo.</summary>
public sealed record Problema(
    string? Type,
    string? Title,
    string? Detail,
    int? Status,
    string? Code,
    Dictionary<string, string[]>? Errors);

public sealed record Resposta<T>(HttpStatusCode StatusCode, T? Valor, Problema? Problema)
{
    public bool Sucesso => Problema is null;

    public static Resposta<T> Ok(HttpStatusCode status, T valor) => new(status, valor, null);
    public static Resposta<T> Erro(HttpStatusCode status, Problema problema) => new(status, default, problema);
}
