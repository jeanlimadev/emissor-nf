using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Nfe.Application.Emissao;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using Emissor.Modules.Nfe.Infrastructure.Sefaz;

namespace Emissor.Modules.Nfe.Application.Consulta;

public sealed class GetNfeHandler(INfeRepository notas)
{
    public async Task<Result<NfeResponse>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var nota = await notas.GetAsync(id, cancellationToken);

        return nota is null
            ? Result<NfeResponse>.Failure(NfeErrors.NaoEncontrada)
            : Result<NfeResponse>.Success(NfeMapping.ToResponse(nota));
    }
}

/// <summary>O XML da nota: o procNFe quando a SEFAZ ja respondeu com protocolo; senao, o XML assinado que foi (ou sera) transmitido.</summary>
public sealed class GetNfeXmlHandler(INfeRepository notas)
{
    public async Task<Result<string>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var nota = await notas.GetAsync(id, cancellationToken);

        if (nota is null)
            return Result<string>.Failure(NfeErrors.NaoEncontrada);

        var xml = nota.XmlProcNfe ?? nota.XmlAssinado;

        return xml is null
            ? Result<string>.Failure(NfeErrors.XmlIndisponivel)
            : Result<string>.Success(xml);
    }
}

public sealed class ListNfesHandler(INfeRepository notas)
{
    public async Task<PagedResponse<NfeResponse>> HandleAsync(Guid? emitenteId, NfeStatus? status, PageRequest? page, CancellationToken cancellationToken)
    {
        var resultado = await notas.ListAsync(emitenteId, status, page, cancellationToken);

        return PagedResponse<NfeResponse>.From(page, resultado.Items.Select(NfeMapping.ToResponse).ToList(), resultado.Total);
    }
}

/// <summary>Espera a nota chegar a um status final, por ate N segundos, para o cliente nao precisar fazer polling.</summary>
public sealed class AguardarNfeHandler(INfeRepository notas)
{
    public const int MaxSegundos = 60;
    private static readonly TimeSpan Intervalo = TimeSpan.FromMilliseconds(500);

    public async Task<Result<NfeResponse>> HandleAsync(Guid id, int segundos, CancellationToken cancellationToken)
    {
        var limite = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Clamp(segundos, 0, MaxSegundos));

        while (true)
        {
            // Sem rastreio: o worker grava por outro DbContext, e a entidade rastreada aqui nao veria a mudanca.
            var nota = await notas.GetSemRastreioAsync(id, cancellationToken);

            if (nota is null)
                return Result<NfeResponse>.Failure(NfeErrors.NaoEncontrada);

            if (nota.EhFinal || DateTimeOffset.UtcNow >= limite)
                return Result<NfeResponse>.Success(NfeMapping.ToResponse(nota));

            await Task.Delay(Intervalo, cancellationToken);
        }
    }
}

public sealed record ReenviarNfeCommand(Guid NfeId, EmissaoNfeRequest? PedidoCorrigido);

/// <summary>Nota rejeitada ou com falha volta para a fila, com o pedido corrigido ou o mesmo. Numero e serie ficam.</summary>
public sealed class ReenviarNfeHandler(
    INfeRepository notas,
    IEmitenteRepository emitentes,
    FluentValidation.IValidator<EmissaoNfeRequest> validador,
    IClock clock)
{
    public async Task<Result<NfeResponse>> HandleAsync(ReenviarNfeCommand command, CancellationToken cancellationToken)
    {
        var nota = await notas.GetAsync(command.NfeId, cancellationToken);

        if (nota is null)
            return Result<NfeResponse>.Failure(NfeErrors.NaoEncontrada);

        var emitente = await emitentes.GetAsync(nota.EmitenteId, cancellationToken);

        if (emitente is null)
            return Result<NfeResponse>.Failure(NfeErrors.EmitenteNaoEncontrado);

        string? pedidoJson = null;
        string? pedidoHash = null;

        if (command.PedidoCorrigido is not null)
        {
            var validacao = await validador.ValidateAsync(command.PedidoCorrigido, cancellationToken);

            if (!validacao.IsValid)
                return Result<NfeResponse>.Failure(EmissaoNfeRequestValidator.ParaErro(validacao));

            if (command.PedidoCorrigido.Identificacao.Serie != nota.Serie)
                return Result<NfeResponse>.Failure(Error.Validation("nfe.reenvio_serie_diferente", "O reenvio mantém a série da nota; para outra série, emita uma nota nova."));

            pedidoJson = NfeMapping.SerializarPedido(command.PedidoCorrigido);
            pedidoHash = NfeMapping.HashDoPedido(pedidoJson);
        }

        var reenvio = nota.Reenviar(pedidoJson, pedidoHash, clock.UtcNow);

        if (reenvio.IsFailure)
            return reenvio.FailAs<NfeResponse>();

        await notas.SaveChangesAsync(cancellationToken);

        return Result<NfeResponse>.Success(NfeMapping.ToResponse(nota));
    }
}

/// <summary>NfeStatusServico4 do autorizador do emitente, com o certificado dele. Bom primeiro teste de credenciamento.</summary>
public sealed class ConsultarStatusServicoHandler(
    IEmitenteRepository emitentes,
    ISefazGateway sefaz,
    ICertificadoAbridor certificados)
{
    public async Task<Result<StatusServicoSefazResponse>> HandleAsync(Guid emitenteId, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(emitenteId, cancellationToken);

        if (emitente is null)
            return Result<StatusServicoSefazResponse>.Failure(NfeErrors.EmitenteNaoEncontrado);

        if (emitente.Certificado is null)
            return Result<StatusServicoSefazResponse>.Failure(NfeErrors.EmitenteSemCertificado);

        var certificado = certificados.Abrir(emitente.Certificado);

        if (certificado.IsFailure)
            return certificado.FailAs<StatusServicoSefazResponse>();

        using var x509 = certificado.Value!;
        var destino = DestinoSefaz.Do(emitente);

        try
        {
            var status = await sefaz.ConsultarStatusServicoAsync(destino, x509, cancellationToken);

            return Result<StatusServicoSefazResponse>.Success(new StatusServicoSefazResponse(
                status.CStat,
                status.XMotivo,
                emitente.Ambiente,
                EnderecosSefaz.Autorizador(emitente.Endereco.Uf),
                status.Recebimento,
                status.TempoMedioSegundos));
        }
        catch (SefazIndisponivelException ex)
        {
            return Result<StatusServicoSefazResponse>.Failure(NfeErrors.SefazIndisponivel with { Message = ex.Message });
        }
    }
}

/// <summary>
/// A DANFE so existe para nota autorizada (ou denegada, que tambem tem
/// protocolo): e o que o Manual de Orientacao permite imprimir fora de
/// contingencia. Para conferir antes, existe a previa em PDF.
/// </summary>
public sealed class GetDanfeHandler(INfeRepository notas, IDanfeRenderer renderer)
{
    public async Task<Result<byte[]>> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var nota = await notas.GetSemRastreioAsync(id, cancellationToken);

        if (nota is null)
            return Result<byte[]>.Failure(NfeErrors.NaoEncontrada);

        if (nota.XmlProcNfe is null || nota.Status is not (NfeStatus.Autorizada or NfeStatus.Denegada or NfeStatus.Cancelada))
            return Result<byte[]>.Failure(NfeErrors.DanfeIndisponivel);

        var marca = nota.Status switch
        {
            NfeStatus.Denegada => "NF-e DENEGADA",
            NfeStatus.Cancelada => "NF-e CANCELADA",
            _ => null
        };

        return Result<byte[]>.Success(renderer.Renderizar(nota.XmlProcNfe, marca));
    }
}
