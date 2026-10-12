using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Emitentes.Domain;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Application.Webhooks;
using Emissor.Modules.Nfe.Domain;
using Emissor.Modules.Nfe.Infrastructure.Sefaz;
using Microsoft.Extensions.Logging;

namespace Emissor.Modules.Nfe.Application.Eventos;

/// <summary>
/// O que cancelamento e carta de correcao tem em comum: montar o evento,
/// assinar, validar no schema, transmitir de forma sincrona e guardar o
/// retorno. A SEFAZ responde eventos na hora, entao nao ha fila: o cliente
/// recebe o resultado na propria chamada. Sem resposta conclusiva, o evento
/// fica Pendente e a mesma chamada, repetida, retoma de onde parou (mesma
/// sequencia, mesmo XML), nunca criando um segundo evento.
/// </summary>
public sealed class TransmissorDeEventos(
    IEmitenteRepository emitentes,
    INfeAssinador assinador,
    INfeXsdValidator xsd,
    ISefazGateway sefaz,
    ICertificadoAbridor certificados,
    ILogger<TransmissorDeEventos> logger)
{
    public async Task<Result> TransmitirAsync(EventoNfe evento, NotaFiscal nota, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(nota.EmitenteId, cancellationToken);

        if (emitente is null)
            return Result.Failure(NfeErrors.EmitenteNaoEncontrado);

        if (emitente.Certificado is null)
            return Result.Failure(NfeErrors.EmitenteSemCertificado);

        var certificado = certificados.Abrir(emitente.Certificado);

        if (certificado.IsFailure)
            return Result.Failure(certificado.Error!);

        using var x509 = certificado.Value!;

        if (evento.XmlEvento is null)
        {
            var dataEvento = Uf.NoFuso(evento.DataEvento, emitente.Endereco.Uf);
            var xml = assinador.AssinarElemento(MensagensEventos.Evento(evento, nota, emitente, nota.ChaveAcesso!, dataEvento), "infEvento", x509);
            var schema = xsd.ValidarEvento(MensagensEventos.EnvEvento(1, xml), evento.Tipo);

            if (schema.IsFailure)
                return schema;

            evento.DefinirXmlAssinado(xml);
        }

        try
        {
            var retorno = await sefaz.EnviarEventoAsync(DestinoSefaz.Do(emitente), x509, evento.XmlEvento!, IdLote(evento), cancellationToken);

            if (retorno.CStat is null)
            {
                // O lote foi recusado antes de olhar o evento (ex.: 128 so chega com retEvento; 2xx/5xx sem retEvento e rejeicao do lote).
                evento.RegistrarRetorno(retorno.CStatLote, retorno.XMotivoLote, null, null, null);
                return Result.Success();
            }

            evento.RegistrarRetorno(
                retorno.CStat.Value,
                retorno.XMotivo ?? string.Empty,
                retorno.Protocolo,
                retorno.Registro,
                retorno.XmlRetEvento is null ? null : MensagensEventos.ProcEventoNFe(evento.XmlEvento!, retorno.XmlRetEvento));

            return Result.Success();
        }
        catch (SefazIndisponivelException ex)
        {
            logger.LogWarning(ex, "SEFAZ indisponível ao transmitir o evento {EventoId} da nota {NfeId}.", evento.Id, nota.Id);
            return Result.Failure(NfeErrors.SefazIndisponivel with { Message = ex.Message + " O evento ficou pendente: repita a chamada." });
        }
    }

    private static long IdLote(EventoNfe evento) => Math.Abs(evento.Id.GetHashCode()) % 1_000_000_000L * 100 + evento.Sequencia;
}

public sealed record CancelarNfeCommand(Guid NfeId, string? Justificativa);

public sealed class CancelarNfeHandler(
    INfeRepository notas,
    IEventoRepository eventos,
    TransmissorDeEventos transmissor,
    AgendadorDeWebhook webhooks,
    IClock clock)
{
    public async Task<Result<EventoNfeResponse>> HandleAsync(CancelarNfeCommand command, CancellationToken cancellationToken)
    {
        var nota = await notas.GetAsync(command.NfeId, cancellationToken);

        if (nota is null)
            return Result<EventoNfeResponse>.Failure(NfeErrors.NaoEncontrada);

        var existentes = await eventos.ListEventosAsync(nota.Id, cancellationToken);
        var evento = existentes.FirstOrDefault(e => e.Tipo == TipoEventoNfe.Cancelamento && e.Status == EventoStatus.Pendente);

        if (evento is null)
        {
            var novo = EventoNfe.Cancelamento(nota, command.Justificativa, clock.UtcNow);

            if (novo.IsFailure)
                return novo.FailAs<EventoNfeResponse>();

            evento = novo.Value!;
            await eventos.AddEventoAsync(evento, cancellationToken);
        }

        var transmitido = await transmissor.TransmitirAsync(evento, nota, cancellationToken);
        // Pendente ou com retorno: grava o que se sabe antes de responder.
        await eventos.SaveChangesAsync(cancellationToken);

        if (transmitido.IsFailure)
            return transmitido.FailAs<EventoNfeResponse>();

        if (evento.Status == EventoStatus.Registrado)
        {
            nota.RegistrarCancelamento(evento, clock.UtcNow);
            await webhooks.AgendarParaNotaAsync(nota, cancellationToken);
            await notas.SaveChangesAsync(cancellationToken);
        }

        return Result<EventoNfeResponse>.Success(EventoMapping.ToResponse(evento));
    }
}

public sealed record CartaCorrecaoCommand(Guid NfeId, string? Correcao);

public sealed class EnviarCartaCorrecaoHandler(
    INfeRepository notas,
    IEventoRepository eventos,
    TransmissorDeEventos transmissor,
    AgendadorDeWebhook webhooks,
    IClock clock)
{
    public async Task<Result<EventoNfeResponse>> HandleAsync(CartaCorrecaoCommand command, CancellationToken cancellationToken)
    {
        var nota = await notas.GetAsync(command.NfeId, cancellationToken);

        if (nota is null)
            return Result<EventoNfeResponse>.Failure(NfeErrors.NaoEncontrada);

        var existentes = (await eventos.ListEventosAsync(nota.Id, cancellationToken)).Where(e => e.Tipo == TipoEventoNfe.CartaCorrecao).ToList();
        var evento = existentes.FirstOrDefault(e => e.Status == EventoStatus.Pendente);

        if (evento is null)
        {
            // A sequencia conta so as cartas que a SEFAZ registrou: rejeitada nao consome numero.
            var registradas = existentes.Count(e => e.Status == EventoStatus.Registrado);
            var novo = EventoNfe.CartaCorrecao(nota, command.Correcao, registradas, clock.UtcNow);

            if (novo.IsFailure)
                return novo.FailAs<EventoNfeResponse>();

            evento = novo.Value!;
            await eventos.AddEventoAsync(evento, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(command.Correcao) && evento.Texto != string.Join(' ', command.Correcao.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
        {
            return Result<EventoNfeResponse>.Failure(NfeErrors.EventoEmAndamento);
        }

        var transmitido = await transmissor.TransmitirAsync(evento, nota, cancellationToken);
        await eventos.SaveChangesAsync(cancellationToken);

        if (transmitido.IsFailure)
            return transmitido.FailAs<EventoNfeResponse>();

        if (evento.Status == EventoStatus.Registrado)
        {
            await webhooks.AgendarAsync(EventosWebhook.CartaCorrecao, nota.Id, Emissao.NfeMapping.ToResponse(nota), cancellationToken);
            await notas.SaveChangesAsync(cancellationToken);
        }

        return Result<EventoNfeResponse>.Success(EventoMapping.ToResponse(evento));
    }
}

public sealed class ListarEventosHandler(INfeRepository notas, IEventoRepository eventos)
{
    public async Task<Result<IReadOnlyList<EventoNfeResponse>>> HandleAsync(Guid nfeId, CancellationToken cancellationToken)
    {
        if (await notas.GetSemRastreioAsync(nfeId, cancellationToken) is null)
            return Result<IReadOnlyList<EventoNfeResponse>>.Failure(NfeErrors.NaoEncontrada);

        var lista = await eventos.ListEventosAsync(nfeId, cancellationToken);

        return Result<IReadOnlyList<EventoNfeResponse>>.Success(lista.Select(EventoMapping.ToResponse).ToList());
    }
}

public sealed class GetEventoXmlHandler(IEventoRepository eventos)
{
    public async Task<Result<string>> HandleAsync(Guid nfeId, Guid eventoId, CancellationToken cancellationToken)
    {
        var evento = await eventos.GetEventoAsync(nfeId, eventoId, cancellationToken);

        if (evento is null)
            return Result<string>.Failure(NfeErrors.EventoNaoEncontrado);

        var xml = evento.XmlProcEvento ?? evento.XmlEvento;

        return xml is null ? Result<string>.Failure(NfeErrors.XmlIndisponivel) : Result<string>.Success(xml);
    }
}

public sealed record InutilizarCommand(Guid EmitenteId, InutilizacaoRequest Pedido);

/// <summary>
/// Inutiliza uma faixa de numeros nunca usados. Sincrono, como os eventos.
/// Homologada, avanca o proximo numero da serie se a faixa o alcancar, para a
/// serie nao emitir um numero inutilizado.
/// </summary>
public sealed class InutilizarHandler(
    IEmitenteRepository emitentes,
    ISerieRepository series,
    IEventoRepository eventos,
    INfeAssinador assinador,
    INfeXsdValidator xsd,
    ISefazGateway sefaz,
    ICertificadoAbridor certificados,
    ICurrentTenant tenant,
    IClock clock,
    ILogger<InutilizarHandler> logger)
{
    public async Task<Result<InutilizacaoResponse>> HandleAsync(InutilizarCommand command, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(command.EmitenteId, cancellationToken);

        if (emitente is null)
            return Result<InutilizacaoResponse>.Failure(NfeErrors.EmitenteNaoEncontrado);

        if (emitente.Certificado is null)
            return Result<InutilizacaoResponse>.Failure(NfeErrors.EmitenteSemCertificado);

        if (emitente.EmContingencia)
            return Result<InutilizacaoResponse>.Failure(NfeErrors.InutilizacaoEmContingencia);

        var pedido = command.Pedido;
        var criada = Inutilizacao.Criar(tenant.TenantId, emitente.Id, emitente.Tipo, pedido.Serie, pedido.NumeroInicial, pedido.NumeroFinal, pedido.Justificativa, clock.UtcNow);

        if (criada.IsFailure)
            return criada.FailAs<InutilizacaoResponse>();

        var inutilizacao = criada.Value!;

        if (await eventos.ExisteNotaNaFaixaAsync(emitente.Id, pedido.Serie, pedido.NumeroInicial, pedido.NumeroFinal, cancellationToken))
            return Result<InutilizacaoResponse>.Failure(NfeErrors.FaixaComNotaEmitida);

        var certificado = certificados.Abrir(emitente.Certificado);

        if (certificado.IsFailure)
            return certificado.FailAs<InutilizacaoResponse>();

        using var x509 = certificado.Value!;
        var xml = assinador.AssinarElemento(MensagensEventos.InutNFe(inutilizacao, emitente), "infInut", x509);
        var schema = xsd.ValidarInutilizacao(xml);

        if (schema.IsFailure)
            return schema.FailAs<InutilizacaoResponse>();

        inutilizacao.DefinirXmlAssinado(xml);
        await eventos.AddInutilizacaoAsync(inutilizacao, cancellationToken);

        try
        {
            var retorno = await sefaz.InutilizarAsync(DestinoSefaz.Do(emitente), x509, xml, cancellationToken);
            inutilizacao.RegistrarRetorno(retorno.CStat, retorno.XMotivo, retorno.Protocolo, retorno.Recebimento,
                retorno.XmlRetInut is null ? null : MensagensEventos.ProcInutNFe(xml, retorno.XmlRetInut));
        }
        catch (SefazIndisponivelException ex)
        {
            logger.LogWarning(ex, "SEFAZ indisponível ao inutilizar a faixa {Inicial}-{Final} da série {Serie}.", pedido.NumeroInicial, pedido.NumeroFinal, pedido.Serie);
            await eventos.SaveChangesAsync(cancellationToken);
            return Result<InutilizacaoResponse>.Failure(NfeErrors.SefazIndisponivel with { Message = ex.Message + " A inutilização ficou pendente; consulte a lista e refaça o pedido se não constar." });
        }

        await eventos.SaveChangesAsync(cancellationToken);

        if (inutilizacao.Status == EventoStatus.Registrado)
        {
            var serie = (await series.ListByEmitenteAsync(emitente.Id, cancellationToken)).FirstOrDefault(s => s.Modelo == Serie.ModeloNfe && s.Numero == pedido.Serie);

            if (serie is not null && serie.ProximoNumero <= pedido.NumeroFinal)
            {
                serie.AjustarProximoNumero(pedido.NumeroFinal + 1);
                await series.SaveChangesAsync(cancellationToken);
            }
        }

        return Result<InutilizacaoResponse>.Success(EventoMapping.ToResponse(inutilizacao));
    }
}

public sealed class ListarInutilizacoesHandler(IEmitenteRepository emitentes, IEventoRepository eventos)
{
    public async Task<Result<IReadOnlyList<InutilizacaoResponse>>> HandleAsync(Guid emitenteId, CancellationToken cancellationToken)
    {
        if (await emitentes.GetAsync(emitenteId, cancellationToken) is null)
            return Result<IReadOnlyList<InutilizacaoResponse>>.Failure(NfeErrors.EmitenteNaoEncontrado);

        var lista = await eventos.ListInutilizacoesAsync(emitenteId, cancellationToken);

        return Result<IReadOnlyList<InutilizacaoResponse>>.Success(lista.Select(EventoMapping.ToResponse).ToList());
    }
}

public sealed class GetInutilizacaoXmlHandler(IEventoRepository eventos)
{
    public async Task<Result<string>> HandleAsync(Guid emitenteId, Guid id, CancellationToken cancellationToken)
    {
        var inutilizacao = await eventos.GetInutilizacaoAsync(emitenteId, id, cancellationToken);

        if (inutilizacao is null)
            return Result<string>.Failure(NfeErrors.InutilizacaoNaoEncontrada);

        var xml = inutilizacao.XmlProcInutilizacao ?? inutilizacao.XmlInutilizacao;

        return xml is null ? Result<string>.Failure(NfeErrors.XmlIndisponivel) : Result<string>.Success(xml);
    }
}

internal static class EventoMapping
{
    public static EventoNfeResponse ToResponse(EventoNfe e) =>
        new(e.Id, e.NfeId, e.Tipo, e.Sequencia, e.Status, e.Texto, e.Protocolo, e.CStat, e.XMotivo, e.DataEvento, e.RegistradoEm, e.CriadoEm);

    public static InutilizacaoResponse ToResponse(Inutilizacao i) =>
        new(i.Id, i.EmitenteId, i.Serie, i.NumeroInicial, i.NumeroFinal, i.Ano, i.Status, i.Justificativa, i.Protocolo, i.CStat, i.XMotivo, i.HomologadaEm, i.CriadaEm);
}
