using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Emitentes.Domain;
using Emissor.Modules.Nfe.Domain;

namespace Emissor.Modules.Nfe.Application.Emissao;

/// <summary>
/// Monta e assina a nota sem transmitir nem gravar: usa o proximo numero da
/// serie, mas nao o consome. Serve para o sistema cliente conferir o XML que
/// sairia e para aprender o layout olhando o resultado.
/// </summary>
public sealed class GerarPreviaNfeHandler(
    IEmitenteRepository emitentes,
    ISerieRepository series,
    MontadorDeNfe montador,
    Ports.IDanfeRenderer danfe)
{
    public const string MarcaPrevia = "PRÉVIA - SEM VALOR FISCAL";

    /// <summary>A mesma previa, desenhada como DANFE com a marca de previa.</summary>
    public async Task<Result<byte[]>> HandleDanfeAsync(Guid emitenteId, EmissaoNfeRequest pedido, CancellationToken cancellationToken)
    {
        var previa = await HandleAsync(emitenteId, pedido, cancellationToken);

        return previa.IsFailure
            ? previa.FailAs<byte[]>()
            : Result<byte[]>.Success(danfe.Renderizar(previa.Value!.Xml, MarcaPrevia));
    }

    public async Task<Result<NfePreviaResponse>> HandleAsync(Guid emitenteId, EmissaoNfeRequest pedido, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(emitenteId, cancellationToken);

        if (emitente is null)
            return Result<NfePreviaResponse>.Failure(NfeErrors.EmitenteNaoEncontrado);

        var serie = (await series.ListByEmitenteAsync(emitenteId, cancellationToken))
            .FirstOrDefault(s => s.Modelo == Serie.ModeloNfe && s.Numero == pedido.Identificacao?.Serie);

        if (serie is null)
            return Result<NfePreviaResponse>.Failure(NfeErrors.SerieNaoEncontrada);

        if (!serie.Ativa)
            return Result<NfePreviaResponse>.Failure(NfeErrors.SerieInativa);

        var montagem = await montador.MontarAsync(emitente, serie.Numero, serie.ProximoNumero, pedido, cancellationToken);

        if (montagem.IsFailure)
            return montagem.FailAs<NfePreviaResponse>();

        var nota = montagem.Value!;

        return Result<NfePreviaResponse>.Success(new NfePreviaResponse(
            nota.Chave.Valor,
            nota.Chave.Serie,
            nota.Chave.Numero,
            nota.DataEmissao,
            nota.Xml));
    }
}
