using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;

namespace Emissor.Modules.Nfe.Domain;

/// <summary>
/// O webhook do tenant (um por tenant no MVP). O segredo fica criptografado
/// com a chave mestra do servico; so sai em texto na resposta da
/// configuracao.
/// </summary>
public sealed class Webhook : ITenantOwned
{
    public const int UrlMaxLength = 500;

    private Webhook() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public byte[] SegredoProtegido { get; private set; } = [];
    public bool Ativo { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }

    public static Result<Webhook> Criar(Guid tenantId, string? url, byte[] segredoProtegido, DateTimeOffset agora)
    {
        var webhook = new Webhook { Id = Guid.CreateVersion7(), TenantId = tenantId, Ativo = true, CriadoEm = agora };
        var atualizado = webhook.Atualizar(url, segredoProtegido, agora);

        return atualizado.IsFailure ? atualizado.FailAs<Webhook>() : Result<Webhook>.Success(webhook);
    }

    public Result Atualizar(string? url, byte[] segredoProtegido, DateTimeOffset agora)
    {
        var valida = ValidarUrl(url);

        if (valida.IsFailure)
            return Result.Failure(valida.Error!);

        Url = valida.Value!;
        SegredoProtegido = segredoProtegido;
        Ativo = true;
        AtualizadoEm = agora;

        return Result.Success();
    }

    /// <summary>So HTTPS fora do localhost: o corpo leva dados fiscais. HTTP em localhost e para desenvolvimento.</summary>
    public static Result<string> ValidarUrl(string? url)
    {
        var limpa = (url ?? string.Empty).Trim();

        if (limpa.Length is 0 or > UrlMaxLength || !Uri.TryCreate(limpa, UriKind.Absolute, out var uri))
            return Result<string>.Failure(NfeErrors.WebhookUrlInvalida);

        var local = uri.IsLoopback;

        if (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && local))
            return Result<string>.Failure(NfeErrors.WebhookUrlInvalida);

        return Result<string>.Success(uri.ToString());
    }
}

/// <summary>
/// Uma entrega: um evento para a URL do webhook, com o corpo ja montado no
/// momento do evento (o que o cliente recebe e o que aconteceu, nao o estado
/// atual). Retentativas com espera crescente ate desistir.
/// </summary>
public sealed class WebhookEntrega : ITenantOwned
{
    public const int MaxTentativas = 10;
    public const int ErroMaxLength = 500;

    private WebhookEntrega() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid WebhookId { get; private set; }
    public Guid? NfeId { get; private set; }
    public string Evento { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public WebhookEntregaStatus Status { get; private set; }
    public int Tentativas { get; private set; }
    public int? UltimoHttpStatus { get; private set; }
    public string? UltimoErro { get; private set; }
    public DateTimeOffset? ProximaTentativaEm { get; private set; }
    public DateTimeOffset CriadaEm { get; private set; }
    public DateTimeOffset? EntregueEm { get; private set; }

    public static WebhookEntrega Agendar(Guid tenantId, Guid webhookId, Guid? nfeId, string evento, Guid entregaId, string payload, DateTimeOffset agora) =>
        new()
        {
            Id = entregaId,
            TenantId = tenantId,
            WebhookId = webhookId,
            NfeId = nfeId,
            Evento = evento,
            Payload = payload,
            Status = WebhookEntregaStatus.Pendente,
            ProximaTentativaEm = agora,
            CriadaEm = agora
        };

    public void RegistrarSucesso(int httpStatus, DateTimeOffset agora)
    {
        Status = WebhookEntregaStatus.Entregue;
        UltimoHttpStatus = httpStatus;
        UltimoErro = null;
        ProximaTentativaEm = null;
        EntregueEm = agora;
    }

    public void RegistrarFalha(int? httpStatus, string erro, TimeSpan espera, DateTimeOffset agora)
    {
        UltimoHttpStatus = httpStatus;
        UltimoErro = erro.Length <= ErroMaxLength ? erro : erro[..ErroMaxLength];

        if (Tentativas >= MaxTentativas)
        {
            Status = WebhookEntregaStatus.Falha;
            ProximaTentativaEm = null;
            return;
        }

        Status = WebhookEntregaStatus.Pendente;
        ProximaTentativaEm = agora + espera;
    }
}

/// <summary>Nomes dos eventos do webhook, derivados do status final da nota.</summary>
public static class EventosWebhook
{
    public const string Teste = "webhook.teste";
    public const string CartaCorrecao = "nfe.carta_correcao";

    public static string? DoStatus(NfeStatus status) => status switch
    {
        NfeStatus.Autorizada => "nfe.autorizada",
        NfeStatus.Rejeitada => "nfe.rejeitada",
        NfeStatus.Denegada => "nfe.denegada",
        NfeStatus.Falha => "nfe.falha",
        NfeStatus.Cancelada => "nfe.cancelada",
        _ => null
    };
}
