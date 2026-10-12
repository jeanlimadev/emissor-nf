using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Emitentes.Domain;

namespace Emissor.Modules.Emitentes.Application.Emitentes;

public sealed class CreateEmitenteHandler(IEmitenteRepository emitentes, ICurrentTenant tenant, IClock clock)
{
    public async Task<Result<EmitenteResponse>> HandleAsync(EmitenteRequest request, CancellationToken cancellationToken)
    {
        var dados = EmitenteMapping.ToDados(request);

        if (dados.IsFailure)
            return dados.FailAs<EmitenteResponse>();

        var emitente = Emitente.Criar(tenant.TenantId, dados.Value!, clock.UtcNow);

        if (emitente.IsFailure)
            return emitente.FailAs<EmitenteResponse>();

        if (await emitentes.ExisteDocumentoAsync(emitente.Value!.Documento, null, cancellationToken))
            return Result<EmitenteResponse>.Failure(EmitenteErrors.DocumentoDuplicado);

        await emitentes.AddAsync(emitente.Value, cancellationToken);
        await emitentes.SaveChangesAsync(cancellationToken);

        return Result<EmitenteResponse>.Success(EmitenteMapping.ToResponse(emitente.Value));
    }
}

public sealed class UpdateEmitenteHandler(IEmitenteRepository emitentes, IClock clock)
{
    public async Task<Result<EmitenteResponse>> HandleAsync(Guid id, EmitenteRequest request, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(id, cancellationToken);

        if (emitente is null)
            return Result<EmitenteResponse>.Failure(EmitenteErrors.NaoEncontrado);

        var dados = EmitenteMapping.ToDados(request);

        if (dados.IsFailure)
            return dados.FailAs<EmitenteResponse>();

        var documento = DocumentoFiscal.SomenteCaracteresValidos(request.Documento);

        if (await emitentes.ExisteDocumentoAsync(documento, id, cancellationToken))
            return Result<EmitenteResponse>.Failure(EmitenteErrors.DocumentoDuplicado);

        var atualizado = emitente.Atualizar(dados.Value!, clock.UtcNow);

        if (atualizado.IsFailure)
            return Result<EmitenteResponse>.Failure(atualizado.Error!);

        await emitentes.SaveChangesAsync(cancellationToken);

        return Result<EmitenteResponse>.Success(EmitenteMapping.ToResponse(emitente));
    }
}

public sealed class GetEmitenteHandler(IEmitenteRepository emitentes)
{
    public async Task<Result<EmitenteResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(id, cancellationToken);

        return emitente is null
            ? Result<EmitenteResponse>.Failure(EmitenteErrors.NaoEncontrado)
            : Result<EmitenteResponse>.Success(EmitenteMapping.ToResponse(emitente));
    }
}

public sealed class ListEmitentesHandler(IEmitenteRepository emitentes)
{
    public async Task<PagedResponse<EmitenteResponse>> HandleAsync(PageRequest? page, bool? ativo, CancellationToken cancellationToken)
    {
        var result = await emitentes.ListAsync(page, ativo, cancellationToken);

        return PagedResponse<EmitenteResponse>.From(
            page,
            result.Items.Select(EmitenteMapping.ToResponse).ToList(),
            result.Total);
    }
}

public sealed class SetEmitenteAtivoHandler(IEmitenteRepository emitentes, IClock clock)
{
    public async Task<Result<EmitenteResponse>> HandleAsync(Guid id, bool ativo, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(id, cancellationToken);

        if (emitente is null)
            return Result<EmitenteResponse>.Failure(EmitenteErrors.NaoEncontrado);

        if (ativo)
            emitente.Reativar(clock.UtcNow);
        else
            emitente.Desativar(clock.UtcNow);

        await emitentes.SaveChangesAsync(cancellationToken);

        return Result<EmitenteResponse>.Success(EmitenteMapping.ToResponse(emitente));
    }
}

/// <summary>Liga ou desliga a contingencia SVC-AN do emitente (decisao de quem opera; ver ADR-022).</summary>
public sealed class SetContingenciaHandler(IEmitenteRepository emitentes, IClock clock)
{
    public async Task<Result<EmitenteResponse>> HandleAsync(Guid id, string? justificativa, bool ativar, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(id, cancellationToken);

        if (emitente is null)
            return Result<EmitenteResponse>.Failure(EmitenteErrors.NaoEncontrado);

        if (ativar)
        {
            var ativado = emitente.AtivarContingencia(justificativa, clock.UtcNow);

            if (ativado.IsFailure)
                return ativado.FailAs<EmitenteResponse>();
        }
        else
        {
            emitente.DesativarContingencia(clock.UtcNow);
        }

        await emitentes.SaveChangesAsync(cancellationToken);

        return Result<EmitenteResponse>.Success(EmitenteMapping.ToResponse(emitente));
    }
}
