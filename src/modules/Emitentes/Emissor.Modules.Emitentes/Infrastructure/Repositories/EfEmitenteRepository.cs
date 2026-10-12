using Emissor.BuildingBlocks;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Emitentes.Domain;
using Emissor.Modules.Emitentes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Emissor.Modules.Emitentes.Infrastructure.Repositories;

internal sealed class EfEmitenteRepository(EmitentesDbContext db) : IEmitenteRepository
{
    public Task<Emitente?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Emitentes.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<bool> ExisteDocumentoAsync(string documento, Guid? exceto, CancellationToken cancellationToken) =>
        db.Emitentes.AnyAsync(e => e.Documento == documento && (exceto == null || e.Id != exceto), cancellationToken);

    public async Task<PagedResult<Emitente>> ListAsync(PageRequest? page, bool? ativo, CancellationToken cancellationToken)
    {
        var query = db.Emitentes.AsQueryable();

        if (ativo is { } filtroAtivo)
            query = query.Where(e => e.Ativo == filtroAtivo);

        var ordered = query.OrderBy(e => e.Nome).ThenBy(e => e.Id);

        if (page is null)
            return PagedResult<Emitente>.Whole(await ordered.ToListAsync(cancellationToken));

        var total = await ordered.CountAsync(cancellationToken);
        var items = await ordered.Skip(page.Skip).Take(page.Take).ToListAsync(cancellationToken);

        return new PagedResult<Emitente>(items, total);
    }

    public async Task AddAsync(Emitente emitente, CancellationToken cancellationToken) =>
        await db.Emitentes.AddAsync(emitente, cancellationToken);

    public void AddCertificado(Certificado certificado) => db.Certificados.Add(certificado);

    public void RemoveCertificado(Certificado certificado) => db.Certificados.Remove(certificado);

    public async Task<IReadOnlyList<CertificadoVencendo>> ListarCertificadosVencendoAsync(DateTimeOffset ate, CancellationToken cancellationToken) =>
        await db.Certificados.IgnoreQueryFilters()
            .Where(c => c.ValidoAte <= ate)
            .Join(db.Emitentes.IgnoreQueryFilters().Where(e => e.Ativo), c => c.EmitenteId, e => e.Id,
                (c, e) => new CertificadoVencendo(c.TenantId, e.Id, e.Nome, e.Documento, c.ValidoAte))
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}

internal sealed class EfSerieRepository(EmitentesDbContext db, ICurrentTenant tenant) : ISerieRepository
{
    public async Task<Result<long>> ReservarNumeroAsync(Guid emitenteId, int numeroSerie, CancellationToken cancellationToken)
    {
        // SQL direto porque o EF nao devolve o valor de um UPDATE. O WHERE repete
        // o tenant: consulta crua nao passa pelo filtro global.
        var tenantId = tenant.TenantId;
        var modelo = Serie.ModeloNfe;
        var maximo = Serie.NumeroNotaMax;

        var reservados = await db.Database
            .SqlQuery<long>($"""
                UPDATE emitentes.series
                SET proximo_numero = proximo_numero + 1
                WHERE emitente_id = {emitenteId} AND tenant_id = {tenantId} AND modelo = {modelo} AND numero = {numeroSerie}
                  AND ativa AND proximo_numero <= {maximo}
                RETURNING proximo_numero - 1 AS "Value"
                """)
            .ToListAsync(cancellationToken);

        if (reservados.Count == 1)
            return Result<long>.Success(reservados[0]);

        // Nao reservou: descobrir o motivo para o erro ser util.
        var serie = await db.Series.FirstOrDefaultAsync(s => s.EmitenteId == emitenteId && s.Modelo == modelo && s.Numero == numeroSerie, cancellationToken);

        if (serie is null)
            return Result<long>.Failure(EmitenteErrors.SerieNaoEncontrada);

        return Result<long>.Failure(serie.Ativa ? EmitenteErrors.SerieEsgotada : EmitenteErrors.SerieInativa);
    }

    public Task<Serie?> GetAsync(Guid emitenteId, Guid id, CancellationToken cancellationToken) =>
        db.Series.FirstOrDefaultAsync(s => s.EmitenteId == emitenteId && s.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Serie>> ListByEmitenteAsync(Guid emitenteId, CancellationToken cancellationToken) =>
        await db.Series.Where(s => s.EmitenteId == emitenteId).OrderBy(s => s.Modelo).ThenBy(s => s.Numero).ToListAsync(cancellationToken);

    public Task<bool> ExisteAsync(Guid emitenteId, int modelo, int numero, CancellationToken cancellationToken) =>
        db.Series.AnyAsync(s => s.EmitenteId == emitenteId && s.Modelo == modelo && s.Numero == numero, cancellationToken);

    public async Task AddAsync(Serie serie, CancellationToken cancellationToken) =>
        await db.Series.AddAsync(serie, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
