namespace Emissor.Contracts.Nfe;

/// <summary>
/// Configuracao do webhook do tenant: uma URL que recebe um POST assinado a
/// cada mudanca final de status de nota. O segredo serve para o sistema
/// cliente conferir a assinatura HMAC-SHA256 do corpo (header
/// <c>X-Emissor-Signature</c>).
/// </summary>
/// <param name="Segredo">Opcional: vazio gera um segredo aleatorio, devolvido uma unica vez na resposta.</param>
public sealed record WebhookRequest(string Url, string? Segredo);

public sealed record WebhookResponse(
    string Url,
    bool Ativo,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm);

/// <summary>Resposta da configuracao: traz o segredo em texto, uma unica vez.</summary>
public sealed record WebhookConfiguradoResponse(
    string Url,
    string Segredo,
    bool Ativo,
    DateTimeOffset CriadoEm,
    DateTimeOffset AtualizadoEm);

public enum WebhookEntregaStatus
{
    Pendente = 1,
    Entregue = 2,
    Falha = 3
}

public sealed record WebhookEntregaResponse(
    Guid Id,
    Guid? NfeId,
    string Evento,
    WebhookEntregaStatus Status,
    int Tentativas,
    int? UltimoHttpStatus,
    string? UltimoErro,
    DateTimeOffset? ProximaTentativaEm,
    DateTimeOffset CriadaEm,
    DateTimeOffset? EntregueEm);

/// <summary>
/// O corpo que chega na URL do webhook. <c>Evento</c> e <c>nfe.autorizada</c>,
/// <c>nfe.rejeitada</c>, <c>nfe.denegada</c>, <c>nfe.falha</c>, <c>nfe.cancelada</c>
/// ou <c>webhook.teste</c>. Headers: <c>X-Emissor-Event</c>, <c>X-Emissor-Delivery</c>
/// (id da entrega, para deduplicar), <c>X-Emissor-Timestamp</c> e
/// <c>X-Emissor-Signature: sha256=&lt;hex&gt;</c> (HMAC do corpo com o segredo).
/// </summary>
/// <param name="Certificado">Preenchido no evento <c>emitente.certificado_vencendo</c>.</param>
public sealed record WebhookEventoPayload(
    string Evento,
    Guid EntregaId,
    DateTimeOffset OcorridoEm,
    NfeResponse? Nota,
    Emitentes.CertificadoVencendoDto? Certificado = null);
