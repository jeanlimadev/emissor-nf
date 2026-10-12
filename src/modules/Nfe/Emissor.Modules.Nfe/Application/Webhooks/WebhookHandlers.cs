using System.Text;
using System.Text.Json;
using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Nfe.Application.Emissao;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using Emissor.Modules.Nfe.Infrastructure.Fila;
using Emissor.Modules.Nfe.Infrastructure.Webhooks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Emissor.Modules.Nfe.Application.Webhooks;

/// <summary>Cria ou troca o webhook do tenant. O segredo (informado ou gerado) volta em texto so aqui.</summary>
public sealed class ConfigurarWebhookHandler(IWebhookRepository webhooks, ICertificadoProtector protector, ICurrentTenant tenant, IClock clock)
{
    public async Task<Result<WebhookConfiguradoResponse>> HandleAsync(WebhookRequest request, CancellationToken cancellationToken)
    {
        var segredo = string.IsNullOrWhiteSpace(request.Segredo) ? AssinaturaWebhook.GerarSegredo() : request.Segredo.Trim();

        if (segredo.Length is < 16 or > 128)
            return Result<WebhookConfiguradoResponse>.Failure(NfeErrors.WebhookSegredoInvalido);

        // O mesmo cofre dos certificados: AES-GCM com a chave mestra do servico.
        var segredoProtegido = protector.Proteger(Encoding.UTF8.GetBytes(segredo));
        var agora = clock.UtcNow;
        var existente = await webhooks.GetAsync(cancellationToken);

        if (existente is null)
        {
            var criado = Webhook.Criar(tenant.TenantId, request.Url, segredoProtegido, agora);

            if (criado.IsFailure)
                return criado.FailAs<WebhookConfiguradoResponse>();

            existente = criado.Value!;
            await webhooks.AddAsync(existente, cancellationToken);
        }
        else
        {
            var atualizado = existente.Atualizar(request.Url, segredoProtegido, agora);

            if (atualizado.IsFailure)
                return atualizado.FailAs<WebhookConfiguradoResponse>();
        }

        await webhooks.SaveChangesAsync(cancellationToken);

        return Result<WebhookConfiguradoResponse>.Success(new WebhookConfiguradoResponse(existente.Url, segredo, existente.Ativo, existente.CriadoEm, existente.AtualizadoEm));
    }
}

public sealed class GetWebhookHandler(IWebhookRepository webhooks)
{
    public async Task<Result<WebhookResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var webhook = await webhooks.GetAsync(cancellationToken);

        return webhook is null
            ? Result<WebhookResponse>.Failure(NfeErrors.WebhookNaoConfigurado)
            : Result<WebhookResponse>.Success(new WebhookResponse(webhook.Url, webhook.Ativo, webhook.CriadoEm, webhook.AtualizadoEm));
    }
}

public sealed class RemoverWebhookHandler(IWebhookRepository webhooks)
{
    public async Task<Result> HandleAsync(CancellationToken cancellationToken)
    {
        var webhook = await webhooks.GetAsync(cancellationToken);

        if (webhook is null)
            return Result.Failure(NfeErrors.WebhookNaoConfigurado);

        webhooks.Remove(webhook);
        await webhooks.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed class ListarEntregasHandler(IWebhookRepository webhooks)
{
    public async Task<PagedResponse<WebhookEntregaResponse>> HandleAsync(Guid? nfeId, PageRequest? page, CancellationToken cancellationToken)
    {
        var resultado = await webhooks.ListEntregasAsync(nfeId, page, cancellationToken);

        return PagedResponse<WebhookEntregaResponse>.From(page, resultado.Items.Select(ToResponse).ToList(), resultado.Total);
    }

    public static WebhookEntregaResponse ToResponse(WebhookEntrega e) =>
        new(e.Id, e.NfeId, e.Evento, e.Status, e.Tentativas, e.UltimoHttpStatus, e.UltimoErro, e.ProximaTentativaEm, e.CriadaEm, e.EntregueEm);
}

/// <summary>
/// Monta e enfileira a entrega de um evento. Chamado pelo processamento da
/// nota, no mesmo DbContext: a entrega e gravada junto com o novo status,
/// atomicamente. Sem webhook configurado, nao faz nada.
/// </summary>
public sealed class AgendadorDeWebhook(IWebhookRepository webhooks, ICurrentTenant tenant, IClock clock)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task AgendarParaNotaAsync(NotaFiscal nota, CancellationToken cancellationToken)
    {
        var evento = EventosWebhook.DoStatus(nota.Status);

        if (evento is null)
            return;

        await AgendarAsync(evento, nota.Id, NfeMapping.ToResponse(nota), cancellationToken);
    }

    public Task<WebhookEntrega?> AgendarAsync(string evento, Guid? nfeId, NfeResponse? nota, CancellationToken cancellationToken) =>
        AgendarAsync(evento, nfeId, nota, null, cancellationToken);

    public async Task<WebhookEntrega?> AgendarAsync(string evento, Guid? nfeId, NfeResponse? nota, Contracts.Emitentes.CertificadoVencendoDto? certificado, CancellationToken cancellationToken)
    {
        var webhook = await webhooks.GetAsync(cancellationToken);

        if (webhook is null || !webhook.Ativo)
            return null;

        var agora = clock.UtcNow;
        var entregaId = Guid.CreateVersion7();
        var payload = JsonSerializer.Serialize(new WebhookEventoPayload(evento, entregaId, agora, nota, certificado), Json);
        var entrega = WebhookEntrega.Agendar(tenant.TenantId, webhook.Id, nfeId, evento, entregaId, payload, agora);

        await webhooks.AddEntregaAsync(entrega, cancellationToken);

        return entrega;
    }
}

/// <summary>Enfileira um evento de teste para o cliente conferir URL e assinatura.</summary>
public sealed class TestarWebhookHandler(AgendadorDeWebhook agendador, IWebhookRepository webhooks)
{
    public async Task<Result<WebhookEntregaResponse>> HandleAsync(CancellationToken cancellationToken)
    {
        var entrega = await agendador.AgendarAsync(EventosWebhook.Teste, null, null, cancellationToken);

        if (entrega is null)
            return Result<WebhookEntregaResponse>.Failure(NfeErrors.WebhookNaoConfigurado);

        await webhooks.SaveChangesAsync(cancellationToken);

        return Result<WebhookEntregaResponse>.Success(ListarEntregasHandler.ToResponse(entrega));
    }
}

/// <summary>O trabalho do worker de webhooks sobre uma entrega retirada da fila.</summary>
public sealed class EntregarWebhookHandler(
    IWebhookRepository webhooks,
    IWebhookEntregador entregador,
    ICertificadoProtector protector,
    IOptions<WorkerOptions> opcoes,
    IClock clock,
    ILogger<EntregarWebhookHandler> logger)
{
    public async Task EntregarAsync(Guid entregaId, CancellationToken cancellationToken)
    {
        var entrega = await webhooks.GetEntregaAsync(entregaId, cancellationToken);

        if (entrega is null || entrega.Status != WebhookEntregaStatus.Pendente)
            return;

        var webhook = await webhooks.GetByIdAsync(entrega.WebhookId, cancellationToken);
        var agora = clock.UtcNow;

        if (webhook is null || !webhook.Ativo)
        {
            entrega.RegistrarFalha(null, "Webhook removido ou desativado.", TimeSpan.Zero, agora);
            await webhooks.SaveChangesAsync(cancellationToken);
            return;
        }

        var resultado = await entregador.EntregarAsync(new Uri(webhook.Url), protector.Abrir(webhook.SegredoProtegido), entrega, cancellationToken);

        if (resultado.Sucesso)
            entrega.RegistrarSucesso(resultado.HttpStatus!.Value, agora);
        else
            entrega.RegistrarFalha(resultado.HttpStatus, resultado.Erro ?? "Falha.", opcoes.Value.EsperaParaTentativa(entrega.Tentativas), agora);

        if (entrega.Status == WebhookEntregaStatus.Falha)
            logger.LogWarning("Entrega {EntregaId} do webhook desistiu após {Tentativas} tentativas: {Erro}", entrega.Id, entrega.Tentativas, entrega.UltimoErro);

        await webhooks.SaveChangesAsync(cancellationToken);
    }
}
