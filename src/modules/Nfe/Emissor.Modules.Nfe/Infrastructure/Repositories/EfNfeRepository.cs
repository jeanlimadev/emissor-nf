using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using Emissor.Modules.Nfe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Emissor.Modules.Nfe.Infrastructure.Repositories;

internal sealed class EfNfeRepository(NfeDbContext db) : INfeRepository
{
    public Task<NotaFiscal?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Notas.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public Task<NotaFiscal?> GetSemRastreioAsync(Guid id, CancellationToken cancellationToken) =>
        db.Notas.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);

    public Task<NotaFiscal?> GetByIdempotenciaAsync(Guid emitenteId, string chaveIdempotencia, CancellationToken cancellationToken) =>
        db.Notas.FirstOrDefaultAsync(n => n.EmitenteId == emitenteId && n.ChaveIdempotencia == chaveIdempotencia, cancellationToken);

    public async Task<PagedResult<NotaFiscal>> ListAsync(Guid? emitenteId, NfeStatus? status, PageRequest? page, CancellationToken cancellationToken)
    {
        var query = db.Notas.AsNoTracking().AsQueryable();

        if (emitenteId is not null)
            query = query.Where(n => n.EmitenteId == emitenteId);

        if (status is not null)
            query = query.Where(n => n.Status == status);

        var ordered = query.OrderByDescending(n => n.CriadaEm).ThenByDescending(n => n.Id);

        if (page is null)
            return PagedResult<NotaFiscal>.Whole(await ordered.ToListAsync(cancellationToken));

        var total = await ordered.CountAsync(cancellationToken);
        var items = await ordered.Skip(page.Skip).Take(page.Take).ToListAsync(cancellationToken);

        return new PagedResult<NotaFiscal>(items, total);
    }

    public async Task AddAsync(NotaFiscal nfe, CancellationToken cancellationToken) =>
        await db.Notas.AddAsync(nfe, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

/// <summary>
/// Retira da fila com <c>FOR UPDATE SKIP LOCKED</c>: varios workers podem
/// rodar ao mesmo tempo sem pegar a mesma nota. Atravessa tenants de
/// proposito (a fila e do servico), por isso ignora o filtro global e nunca
/// devolve a entidade, so os ids.
/// </summary>
internal sealed class EfNfeFila(NfeDbContext db) : INfeFila
{
    private static readonly int[] StatusDeFila = [(int)NfeStatus.Recebida, (int)NfeStatus.PendenteConsulta];

    public async Task<TrabalhoNfe?> RetirarAsync(DateTimeOffset agora, TimeSpan posse, CancellationToken cancellationToken)
    {
        var emProcessamento = (int)NfeStatus.EmProcessamento;
        var posseVencida = agora - posse;

        var retirados = await db.Database
            .SqlQuery<TrabalhoNfeLinha>($"""
                UPDATE nfe.notas
                SET status = {emProcessamento}, tentativas = tentativas + 1, proxima_tentativa_em = NULL, atualizada_em = {agora}
                WHERE id = (
                    SELECT id FROM nfe.notas
                    WHERE (status = ANY({StatusDeFila}) AND proxima_tentativa_em <= {agora})
                       OR (status = {emProcessamento} AND atualizada_em <= {posseVencida})
                    ORDER BY proxima_tentativa_em NULLS FIRST, criada_em
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1)
                RETURNING id AS "NfeId", tenant_id AS "TenantId"
                """)
            .ToListAsync(cancellationToken);

        return retirados.Count == 0 ? null : new TrabalhoNfe(retirados[0].NfeId, retirados[0].TenantId);
    }

    private sealed record TrabalhoNfeLinha(Guid NfeId, Guid TenantId);
}

internal sealed class EfWebhookRepository(NfeDbContext db) : IWebhookRepository
{
    public Task<Webhook?> GetAsync(CancellationToken cancellationToken) => db.Webhooks.FirstOrDefaultAsync(cancellationToken);

    public Task<Webhook?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => db.Webhooks.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public async Task AddAsync(Webhook webhook, CancellationToken cancellationToken) => await db.Webhooks.AddAsync(webhook, cancellationToken);

    public void Remove(Webhook webhook) => db.Webhooks.Remove(webhook);

    public async Task AddEntregaAsync(WebhookEntrega entrega, CancellationToken cancellationToken) => await db.WebhookEntregas.AddAsync(entrega, cancellationToken);

    public Task<WebhookEntrega?> GetEntregaAsync(Guid id, CancellationToken cancellationToken) => db.WebhookEntregas.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<PagedResult<WebhookEntrega>> ListEntregasAsync(Guid? nfeId, PageRequest? page, CancellationToken cancellationToken)
    {
        var query = db.WebhookEntregas.AsNoTracking().AsQueryable();

        if (nfeId is not null)
            query = query.Where(e => e.NfeId == nfeId);

        var ordered = query.OrderByDescending(e => e.CriadaEm).ThenByDescending(e => e.Id);

        if (page is null)
            return PagedResult<WebhookEntrega>.Whole(await ordered.ToListAsync(cancellationToken));

        var total = await ordered.CountAsync(cancellationToken);
        var items = await ordered.Skip(page.Skip).Take(page.Take).ToListAsync(cancellationToken);

        return new PagedResult<WebhookEntrega>(items, total);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

/// <summary>Fila de entregas de webhook: mesmo desenho da fila de notas, sem posse (a entrega e curta e idempotente pelo id).</summary>
internal sealed class EfWebhookFila(NfeDbContext db) : IWebhookFila
{
    public async Task<TrabalhoWebhook?> RetirarAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var pendente = (int)WebhookEntregaStatus.Pendente;

        var retirados = await db.Database
            .SqlQuery<TrabalhoWebhookLinha>($"""
                UPDATE nfe.webhook_entregas
                SET tentativas = tentativas + 1, proxima_tentativa_em = NULL
                WHERE id = (
                    SELECT id FROM nfe.webhook_entregas
                    WHERE status = {pendente} AND proxima_tentativa_em <= {agora}
                    ORDER BY proxima_tentativa_em, criada_em
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1)
                RETURNING id AS "EntregaId", tenant_id AS "TenantId"
                """)
            .ToListAsync(cancellationToken);

        return retirados.Count == 0 ? null : new TrabalhoWebhook(retirados[0].EntregaId, retirados[0].TenantId);
    }

    private sealed record TrabalhoWebhookLinha(Guid EntregaId, Guid TenantId);
}

internal sealed class EfEventoRepository(NfeDbContext db) : IEventoRepository
{
    public Task<EventoNfe?> GetEventoAsync(Guid nfeId, Guid eventoId, CancellationToken cancellationToken) =>
        db.Eventos.FirstOrDefaultAsync(e => e.NfeId == nfeId && e.Id == eventoId, cancellationToken);

    public async Task<IReadOnlyList<EventoNfe>> ListEventosAsync(Guid nfeId, CancellationToken cancellationToken) =>
        await db.Eventos.Where(e => e.NfeId == nfeId).OrderBy(e => e.CriadoEm).ThenBy(e => e.Sequencia).ToListAsync(cancellationToken);

    public async Task AddEventoAsync(EventoNfe evento, CancellationToken cancellationToken) => await db.Eventos.AddAsync(evento, cancellationToken);

    public Task<Inutilizacao?> GetInutilizacaoAsync(Guid emitenteId, Guid id, CancellationToken cancellationToken) =>
        db.Inutilizacoes.FirstOrDefaultAsync(i => i.EmitenteId == emitenteId && i.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Inutilizacao>> ListInutilizacoesAsync(Guid emitenteId, CancellationToken cancellationToken) =>
        await db.Inutilizacoes.Where(i => i.EmitenteId == emitenteId).OrderByDescending(i => i.CriadaEm).ToListAsync(cancellationToken);

    public async Task AddInutilizacaoAsync(Inutilizacao inutilizacao, CancellationToken cancellationToken) => await db.Inutilizacoes.AddAsync(inutilizacao, cancellationToken);

    public Task<bool> ExisteNotaNaFaixaAsync(Guid emitenteId, int serie, long inicial, long final, CancellationToken cancellationToken) =>
        db.Notas.AnyAsync(n => n.EmitenteId == emitenteId && n.Serie == serie && n.Numero != null && n.Numero >= inicial && n.Numero <= final, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
