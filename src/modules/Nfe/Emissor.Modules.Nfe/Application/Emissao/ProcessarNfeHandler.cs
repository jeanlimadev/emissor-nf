using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Emitentes.Domain;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Domain;
using Emissor.Modules.Nfe.Infrastructure.Fila;
using Emissor.Modules.Nfe.Infrastructure.Sefaz;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Emissor.Modules.Nfe.Application.Emissao;

/// <summary>
/// O trabalho do worker sobre uma nota ja marcada EmProcessamento:
/// <list type="number">
/// <item>Recebida sem numero: reserva o numero, monta e assina o XML, grava.</item>
/// <item>PendenteConsulta: consulta pela chave antes de qualquer reenvio.</item>
/// <item>Transmite (NFeAutorizacao4 sincrono) e interpreta o retorno.</item>
/// </list>
/// Cada passo grava o estado antes de seguir: se o processo cair no meio, a
/// proxima tentativa sabe de onde continuar e nunca gera duas notas na SEFAZ.
/// </summary>
public sealed class ProcessarNfeHandler(
    INfeRepository notas,
    IEmitenteRepository emitentes,
    ISerieRepository series,
    MontadorDeNfe montador,
    ISefazGateway sefaz,
    ICertificadoAbridor certificados,
    Webhooks.AgendadorDeWebhook webhooks,
    IOptions<WorkerOptions> opcoes,
    IClock clock,
    ILogger<ProcessarNfeHandler> logger)
{
    public async Task ProcessarAsync(Guid nfeId, CancellationToken cancellationToken)
    {
        var nota = await notas.GetAsync(nfeId, cancellationToken);

        if (nota is null || nota.Status != NfeStatus.EmProcessamento)
        {
            logger.LogWarning("Nota {NfeId} não está em processamento; nada a fazer.", nfeId);
            return;
        }

        try
        {
            await ExecutarAsync(nota, cancellationToken);
        }
        catch (SefazIndisponivelException ex)
        {
            // Sem resposta conclusiva: a nota pode ou nao ter entrado na SEFAZ. Consulta antes de reenviar.
            logger.LogWarning(ex, "SEFAZ indisponível para a nota {NfeId} (tentativa {Tentativa}).", nota.Id, nota.Tentativas);
            Reagendar(nota, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Erro inesperado ao processar a nota {NfeId} (tentativa {Tentativa}).", nota.Id, nota.Tentativas);
            Reagendar(nota, "Erro interno: " + ex.GetType().Name);
        }

        // Status final? A entrega do webhook entra na mesma gravacao que o novo status.
        if (nota.EhFinal)
            await webhooks.AgendarParaNotaAsync(nota, cancellationToken);

        await notas.SaveChangesAsync(cancellationToken);
    }

    private async Task ExecutarAsync(NotaFiscal nota, CancellationToken cancellationToken)
    {
        var emitente = await emitentes.GetAsync(nota.EmitenteId, cancellationToken);

        if (emitente is null)
        {
            nota.RegistrarFalha("Emitente não encontrado.", clock.UtcNow);
            return;
        }

        if (emitente.Certificado is null)
        {
            nota.RegistrarFalha(NfeErrors.EmitenteSemCertificado.Message, clock.UtcNow);
            return;
        }

        var destino = DestinoSefaz.Do(emitente);

        // 1. Numero e XML (so na primeira passagem desta versao do pedido).
        if (nota.XmlAssinado is null)
        {
            var montado = await MontarAsync(nota, emitente, cancellationToken);

            if (montado.IsFailure)
            {
                nota.RegistrarFalha(montado.Error!.Message, clock.UtcNow);
                return;
            }

            // Grava antes de transmitir: se cair depois, a consulta pela chave encontra a nota.
            await notas.SaveChangesAsync(cancellationToken);
        }

        var chave = ChaveAcesso.Parse(nota.ChaveAcesso).Value!;
        var certificado = certificados.Abrir(emitente.Certificado);

        if (certificado.IsFailure)
        {
            nota.RegistrarFalha(certificado.Error!.Message, clock.UtcNow);
            return;
        }

        using var x509 = certificado.Value!;

        // 2a. A SEFAZ so recebeu o lote (103)? Pergunta pelo recibo: enquanto o lote processa, a consulta
        //     pela chave diria "nao consta" e o reenvio geraria duplicidade.
        if (nota.Recibo is not null)
        {
            var recibo = await sefaz.ConsultarReciboAsync(destino, x509, nota.Recibo, cancellationToken);

            if (TratarRecibo(nota, recibo))
                return;
        }

        // 2b. Ja foi enviada alguma vez? Entao consulta antes de reenviar (rejeicao 204/539 e o que se evita aqui).
        if (nota.Tentativas > 1)
        {
            var consulta = await sefaz.ConsultarPorChaveAsync(destino, x509, chave, cancellationToken);

            if (TratarConsulta(nota, consulta))
                return;
        }

        // 3. Transmite.
        var retorno = await sefaz.AutorizarAsync(destino, x509, nota.XmlAssinado!, IdLote(nota), cancellationToken);
        TratarAutorizacao(nota, retorno);
    }

    private async Task<Result> MontarAsync(NotaFiscal nota, Emitente emitente, CancellationToken cancellationToken)
    {
        var pedido = NfeMapping.DesserializarPedido(nota.PedidoJson);
        long numero;

        if (nota.Numero is { } jaReservado)
        {
            numero = jaReservado;
        }
        else
        {
            var reserva = await series.ReservarNumeroAsync(emitente.Id, nota.Serie, cancellationToken);

            if (reserva.IsFailure)
                return Result.Failure(reserva.Error!);

            numero = reserva.Value!;
        }

        var montagem = await montador.MontarAsync(emitente, nota.Serie, numero, pedido, cancellationToken);

        if (montagem.IsFailure)
            return Result.Failure(montagem.Error!);

        var assinada = montagem.Value!;
        nota.DefinirNumeroEChave(numero, assinada.Chave, assinada.DataEmissao, clock.UtcNow);
        nota.DefinirXmlAssinado(assinada.Xml, clock.UtcNow);

        return Result.Success();
    }

    /// <summary>Devolve true quando o recibo resolveu a nota (ou ainda esta processando: espera mais).</summary>
    private bool TratarRecibo(NotaFiscal nota, RetornoRecibo recibo)
    {
        var agora = clock.UtcNow;

        if (recibo.CStat == CodigosSefaz.LoteEmProcessamento)
        {
            nota.MarcarPendenteConsulta($"{recibo.CStat} - {recibo.XMotivo}", nota.Recibo, opcoes.Value.EsperaParaTentativa(1), agora);
            return true;
        }

        if (recibo.Protocolo is { } protocolo)
        {
            TratarProtocolo(nota, protocolo, agora);
            return true;
        }

        // Recibo desconhecido/expirado (ex.: 225, 656): segue para a consulta pela chave.
        nota.LimparRecibo();
        return false;
    }

    private void TratarProtocolo(NotaFiscal nota, ProtocoloSefaz protocolo, DateTimeOffset agora)
    {
        if (protocolo.CStat == CodigosSefaz.Autorizada)
            nota.RegistrarAutorizacao(protocolo, MensagensSefaz.ProcNFe(nota.XmlAssinado!, protocolo.XmlProtNfe!), agora);
        else if (CodigosSefaz.EhDenegacao(protocolo.CStat))
            nota.RegistrarDenegacao(protocolo, MensagensSefaz.ProcNFe(nota.XmlAssinado!, protocolo.XmlProtNfe!), agora);
        else
            nota.RegistrarRejeicao(protocolo.CStat, protocolo.XMotivo, agora);
    }

    /// <summary>Devolve true quando a consulta resolveu a nota (nao precisa transmitir).</summary>
    private bool TratarConsulta(NotaFiscal nota, RetornoConsulta consulta)
    {
        var agora = clock.UtcNow;

        if (consulta.CStat == CodigosSefaz.Autorizada && consulta.Protocolo is not null)
        {
            nota.RegistrarAutorizacao(consulta.Protocolo, MensagensSefaz.ProcNFe(nota.XmlAssinado!, consulta.Protocolo.XmlProtNfe!), agora);
            return true;
        }

        if (CodigosSefaz.EhDenegacao(consulta.CStat) && consulta.Protocolo is not null)
        {
            nota.RegistrarDenegacao(consulta.Protocolo, MensagensSefaz.ProcNFe(nota.XmlAssinado!, consulta.Protocolo.XmlProtNfe!), agora);
            return true;
        }

        if (consulta.CStat == CodigosSefaz.NaoConstaNaBase)
            return false; // Nunca chegou: pode transmitir.

        if (consulta.CStat == CodigosSefaz.Cancelada)
        {
            nota.RegistrarRejeicao(consulta.CStat, consulta.XMotivo, agora);
            return true;
        }

        // Qualquer outra situacao (servico paralisado, retorno inesperado): tenta de novo mais tarde.
        Reagendar(nota, $"{consulta.CStat} - {consulta.XMotivo}");
        return true;
    }

    private void TratarAutorizacao(NotaFiscal nota, RetornoAutorizacao retorno)
    {
        var agora = clock.UtcNow;
        var protocolo = retorno.Protocolo;

        if (protocolo is not null)
        {
            if (protocolo.CStat == CodigosSefaz.Autorizada)
            {
                nota.RegistrarAutorizacao(protocolo, MensagensSefaz.ProcNFe(nota.XmlAssinado!, protocolo.XmlProtNfe!), agora);
                return;
            }

            if (CodigosSefaz.EhDenegacao(protocolo.CStat))
            {
                nota.RegistrarDenegacao(protocolo, MensagensSefaz.ProcNFe(nota.XmlAssinado!, protocolo.XmlProtNfe!), agora);
                return;
            }

            nota.RegistrarRejeicao(protocolo.CStat, protocolo.XMotivo, agora);
            return;
        }

        // Sem protNFe: o retorno fala do lote, nao da nota.
        if (CodigosSefaz.EhTemporario(retorno.CStat))
        {
            nota.MarcarPendenteConsulta($"{retorno.CStat} - {retorno.XMotivo}", retorno.Recibo, opcoes.Value.EsperaParaTentativa(nota.Tentativas), agora);
            return;
        }

        nota.RegistrarRejeicao(retorno.CStat, retorno.XMotivo, agora);
    }

    private void Reagendar(NotaFiscal nota, string motivo)
    {
        var agora = clock.UtcNow;

        if (nota.EsgotouTentativas)
            nota.RegistrarFalha($"Desistiu após {nota.Tentativas} tentativas. Último motivo: {motivo}", agora);
        else
            nota.MarcarPendenteConsulta(motivo, null, opcoes.Value.EsperaParaTentativa(nota.Tentativas), agora);
    }

    /// <summary>idLote: 1 a 15 digitos, unico por envio. Os ticks de criacao da nota bastam.</summary>
    private static long IdLote(NotaFiscal nota) => Math.Abs(nota.Id.GetHashCode()) % 1_000_000_000L * 1000 + nota.Tentativas % 1000;
}
