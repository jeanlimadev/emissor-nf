using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;
using Emissor.Modules.Emitentes.Application.Emitentes;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Emitentes.Domain;

namespace Emissor.Modules.Emitentes.Application.Series;

public sealed class CreateSerieHandler(IEmitenteRepository emitentes, ISerieRepository series, ICurrentTenant tenant, IClock clock)
{
    public async Task<Result<SerieResponse>> HandleAsync(Guid emitenteId, SerieRequest request, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(emitenteId, cancellationToken);

        if (emitente is null)
            return Result<SerieResponse>.Failure(EmitenteErrors.NaoEncontrado);

        var serie = Serie.Criar(tenant.TenantId, emitente.Id, request.Numero, request.ProximoNumero, clock.UtcNow);

        if (serie.IsFailure)
            return serie.FailAs<SerieResponse>();

        if (await series.ExisteAsync(emitente.Id, Serie.ModeloNfe, request.Numero, cancellationToken))
            return Result<SerieResponse>.Failure(EmitenteErrors.SerieDuplicada);

        await series.AddAsync(serie.Value!, cancellationToken);
        await series.SaveChangesAsync(cancellationToken);

        return Result<SerieResponse>.Success(EmitenteMapping.ToResponse(serie.Value!));
    }
}

public sealed class ListSeriesHandler(IEmitenteRepository emitentes, ISerieRepository series)
{
    public async Task<Result<IReadOnlyList<SerieResponse>>> HandleAsync(Guid emitenteId, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(emitenteId, cancellationToken);

        if (emitente is null)
            return Result<IReadOnlyList<SerieResponse>>.Failure(EmitenteErrors.NaoEncontrado);

        var lista = await series.ListByEmitenteAsync(emitente.Id, cancellationToken);

        return Result<IReadOnlyList<SerieResponse>>.Success(lista.Select(EmitenteMapping.ToResponse).ToList());
    }
}

public sealed record UpdateSerieCommand(Guid EmitenteId, Guid SerieId, long? ProximoNumero, bool? Ativa);

public sealed class UpdateSerieHandler(ISerieRepository series)
{
    public async Task<Result<SerieResponse>> HandleAsync(UpdateSerieCommand command, CancellationToken cancellationToken)
    {
        var serie = await series.GetAsync(command.EmitenteId, command.SerieId, cancellationToken);

        if (serie is null)
            return Result<SerieResponse>.Failure(EmitenteErrors.SerieNaoEncontrada);

        if (command.ProximoNumero is { } proximo)
        {
            var ajuste = serie.AjustarProximoNumero(proximo);

            if (ajuste.IsFailure)
                return Result<SerieResponse>.Failure(ajuste.Error!);
        }

        if (command.Ativa is { } ativa)
        {
            if (ativa)
                serie.Reativar();
            else
                serie.Desativar();
        }

        await series.SaveChangesAsync(cancellationToken);

        return Result<SerieResponse>.Success(EmitenteMapping.ToResponse(serie));
    }
}
