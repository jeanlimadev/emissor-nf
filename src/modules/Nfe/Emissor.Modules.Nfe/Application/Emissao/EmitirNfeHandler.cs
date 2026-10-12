using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Emitentes.Domain;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using FluentValidation;

namespace Emissor.Modules.Nfe.Application.Emissao;

public sealed record EmitirNfeCommand(Guid EmitenteId, string? ChaveIdempotencia, EmissaoNfeRequest Pedido);

/// <summary>
/// Recebe o pedido de emissao: valida o que da para validar sem numero
/// (estrutura, emitente, certificado, serie, mapeamento para o XML), grava a
/// nota como Recebida e devolve. Quem transmite e o worker. A mesma
/// Idempotency-Key com o mesmo pedido devolve a nota ja existente.
/// </summary>
public sealed class EmitirNfeHandler(
    INfeRepository notas,
    IEmitenteRepository emitentes,
    ISerieRepository series,
    IValidator<EmissaoNfeRequest> validador,
    INfeXmlBuilder xmlBuilder,
    IGeradorCodigoNumerico codigoNumerico,
    ICurrentTenant tenant,
    IClock clock)
{
    public async Task<Result<NfeResponse>> HandleAsync(EmitirNfeCommand command, CancellationToken cancellationToken)
    {
        var chaveIdempotencia = (command.ChaveIdempotencia ?? string.Empty).Trim();

        if (chaveIdempotencia.Length is 0 or > NotaFiscal.ChaveIdempotenciaMaxLength)
            return Result<NfeResponse>.Failure(NfeErrors.IdempotenciaAusente);

        var emitente = await emitentes.GetAsync(command.EmitenteId, cancellationToken);

        if (emitente is null)
            return Result<NfeResponse>.Failure(NfeErrors.EmitenteNaoEncontrado);

        var pedidoJson = NfeMapping.SerializarPedido(command.Pedido);
        var pedidoHash = NfeMapping.HashDoPedido(pedidoJson);

        var existente = await notas.GetByIdempotenciaAsync(emitente.Id, chaveIdempotencia, cancellationToken);

        if (existente is not null)
        {
            return existente.PedidoHash == pedidoHash
                ? Result<NfeResponse>.Success(NfeMapping.ToResponse(existente))
                : Result<NfeResponse>.Failure(NfeErrors.IdempotenciaConflito);
        }

        var validacao = await ValidarAsync(emitente, command.Pedido, cancellationToken);

        if (validacao.IsFailure)
            return validacao.FailAs<NfeResponse>();

        var nota = NotaFiscal.Receber(
            tenant.TenantId,
            emitente.Id,
            emitente.Ambiente,
            command.Pedido.Identificacao.Serie,
            chaveIdempotencia,
            pedidoHash,
            pedidoJson,
            clock.UtcNow);

        await notas.AddAsync(nota, cancellationToken);
        await notas.SaveChangesAsync(cancellationToken);

        return Result<NfeResponse>.Success(NfeMapping.ToResponse(nota));
    }

    /// <summary>
    /// Tudo o que pode ser conferido antes de reservar numero. Monta o XML com
    /// uma chave provisoria so para o mapeamento apontar campo faltando agora,
    /// e nao daqui a alguns segundos no worker.
    /// </summary>
    private async Task<Result> ValidarAsync(Emitente emitente, EmissaoNfeRequest pedido, CancellationToken cancellationToken)
    {
        var estrutura = await validador.ValidateAsync(pedido, cancellationToken);

        if (!estrutura.IsValid)
            return Result.Failure(EmissaoNfeRequestValidator.ParaErro(estrutura));

        if (!emitente.Ativo)
            return Result.Failure(NfeErrors.EmitenteInativo);

        if (emitente.Certificado is null)
            return Result.Failure(NfeErrors.EmitenteSemCertificado);

        if (emitente.Certificado.ValidoAte <= clock.UtcNow)
            return Result.Failure(NfeErrors.CertificadoVencido);

        var serie = (await series.ListByEmitenteAsync(emitente.Id, cancellationToken))
            .FirstOrDefault(s => s.Modelo == Serie.ModeloNfe && s.Numero == pedido.Identificacao.Serie);

        if (serie is null)
            return Result.Failure(NfeErrors.SerieNaoEncontrada);

        if (!serie.Ativa)
            return Result.Failure(NfeErrors.SerieInativa);

        var dataEmissao = Uf.NoFuso(pedido.Identificacao.DataEmissao ?? clock.UtcNow, emitente.Endereco.Uf);
        var chaveProvisoria = ChaveAcesso.Criar(Uf.Codigo(emitente.Endereco.Uf), dataEmissao, emitente.Documento, serie.Numero, serie.ProximoNumero, codigoNumerico.Gerar(serie.ProximoNumero));

        if (chaveProvisoria.IsFailure)
            return Result.Failure(chaveProvisoria.Error!);

        var xml = xmlBuilder.Montar(new NfeMontagem(emitente, chaveProvisoria.Value!, dataEmissao, pedido));

        return xml.IsFailure ? Result.Failure(xml.Error!) : Result.Success();
    }
}
