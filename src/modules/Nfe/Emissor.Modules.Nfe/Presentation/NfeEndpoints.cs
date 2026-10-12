using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Nfe.Application.Consulta;
using Emissor.Modules.Nfe.Application.Emissao;
using Emissor.Modules.Nfe.Application.Eventos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Emissor.Modules.Nfe.Presentation;

internal static class NfeEndpoints
{
    public const string HeaderIdempotencia = "Idempotency-Key";
    private const string XmlDeclaration = """<?xml version="1.0" encoding="UTF-8"?>""";

    /// <summary>Rotas sob <c>/api/v1/emitentes/{id}</c>: o que depende do emitente (emitir, previa, status da SEFAZ).</summary>
    internal static RouteGroupBuilder MapNfePorEmitente(this RouteGroupBuilder group)
    {
        group.MapPost("/nfe", async (
            Guid id,
            EmissaoNfeRequest pedido,
            [FromHeader(Name = HeaderIdempotencia)] string? idempotencyKey,
            int? aguardar,
            EmitirNfeHandler emitir,
            AguardarNfeHandler aguardarHandler,
            CancellationToken ct) =>
        {
            var result = await emitir.HandleAsync(new EmitirNfeCommand(id, idempotencyKey, pedido), ct);

            if (result.IsFailure)
                return ProblemResults.FromError(result.Error!, "Não foi possível receber a NF-e.");

            var nota = result.Value!;

            if (aguardar is > 0)
            {
                var final = await aguardarHandler.HandleAsync(nota.Id, aguardar.Value, ct);

                if (final.IsSuccess)
                    nota = final.Value!;
            }

            return nota.Status is NfeStatus.Recebida or NfeStatus.EmProcessamento or NfeStatus.PendenteConsulta
                ? Results.Accepted($"/api/v1/nfe/{nota.Id}", nota)
                : Results.Ok(nota);
        })
        .WithSummary("Emite uma NF-e: valida, enfileira e responde 202. Com `?aguardar=N` espera até N segundos pelo resultado da SEFAZ (200 quando final).")
        .WithDescription("Header `Idempotency-Key` obrigatório: repetir a chamada com a mesma chave e o mesmo pedido devolve a mesma nota.");

        group.MapPost("/nfe/previa", async (Guid id, EmissaoNfeRequest pedido, string? formato, GerarPreviaNfeHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, pedido, ct);

            if (result.IsFailure)
                return ProblemResults.FromError(result.Error!, "Não foi possível montar a NF-e.");

            // ?formato=xml devolve o XML puro, pronto para salvar em arquivo e abrir em visualizadores de DANFE.
            if (string.Equals(formato, "xml", StringComparison.OrdinalIgnoreCase))
                return Results.Text(XmlDeclaration + result.Value!.Xml, "application/xml", System.Text.Encoding.UTF8);

            return Results.Ok(result.Value);
        })
        .WithSummary("Prévia: monta, assina e valida a NF-e contra o schema, sem transmitir e sem consumir número da série.")
        .WithDescription("Devolve a chave de acesso e o XML assinado em JSON; com `?formato=xml`, devolve só o XML. Erros de campo vêm em `errors` (campo -> mensagens).");

        group.MapPost("/nfe/previa/danfe", async (Guid id, EmissaoNfeRequest pedido, GerarPreviaNfeHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleDanfeAsync(id, pedido, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível montar a DANFE.")
                : Results.File(result.Value!, "application/pdf", "danfe-previa.pdf");
        })
        .WithSummary("Prévia em PDF: a DANFE da nota montada, com a marca 'PRÉVIA - SEM VALOR FISCAL'. Não transmite nem consome número.");

        group.MapPost("/inutilizacoes", async (Guid id, InutilizacaoRequest pedido, InutilizarHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new InutilizarCommand(id, pedido), ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível inutilizar a numeração.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Inutiliza uma faixa de números nunca usados (NfeInutilizacao4). Síncrono: a resposta já traz o cStat (102 = homologada).");

        group.MapGet("/inutilizacoes", async (Guid id, ListarInutilizacoesHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Emitente não encontrado.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Inutilizações do emitente, da mais nova para a mais antiga.");

        group.MapGet("/inutilizacoes/{inutilizacaoId:guid}/xml", async (Guid id, Guid inutilizacaoId, GetInutilizacaoXmlHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, inutilizacaoId, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "XML indisponível.")
                : Results.Text(XmlDeclaration + result.Value, "application/xml", System.Text.Encoding.UTF8);
        })
        .WithSummary("XML da inutilização (procInutNFe quando homologada).");

        group.MapGet("/sefaz/status", async (Guid id, ConsultarStatusServicoHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível consultar a SEFAZ.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Status do autorizador (NfeStatusServico4) com o certificado do emitente. 107 = em operação.");

        return group;
    }

    /// <summary>Rotas sob <c>/api/v1/nfe</c>: consulta e reenvio de notas do tenant.</summary>
    internal static RouteGroupBuilder MapNotas(this RouteGroupBuilder group)
    {
        group.MapGet("/", async (Guid? emitenteId, NfeStatus? status, int? page, int? pageSize, ListNfesHandler handler, CancellationToken ct) =>
        {
            if (!PageRequest.TryCreate(page, pageSize, out var request, out var pageSizeInvalid))
                return ProblemResults.FromError(
                    pageSizeInvalid ? PageRequest.InvalidPageSize : PageRequest.InvalidPage,
                    "Parâmetros de paginação inválidos.");

            return Results.Ok(await handler.HandleAsync(emitenteId, status, request, ct));
        })
        .WithSummary("Lista as notas do tenant, da mais nova para a mais antiga. Filtros: emitenteId, status.");

        group.MapGet("/{id:guid}", async (Guid id, GetNfeHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Nota não encontrada.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Situação da nota: status, chave, protocolo, cStat e motivo.");

        group.MapGet("/{id:guid}/xml", async (Guid id, GetNfeXmlHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "XML indisponível.")
                : Results.Text(XmlDeclaration + result.Value, "application/xml", System.Text.Encoding.UTF8);
        })
        .WithSummary("O XML da nota: procNFe (com protocolo) quando autorizada ou denegada; senão o XML assinado.");

        group.MapGet("/{id:guid}/danfe", async (Guid id, GetDanfeHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "DANFE indisponível.")
                : Results.File(result.Value!, "application/pdf", $"danfe-{id}.pdf");
        })
        .WithSummary("DANFE em PDF da nota autorizada (A4 retrato, leiaute do Anexo II do MOC). Em homologação sai com 'SEM VALOR FISCAL'.");

        group.MapPost("/{id:guid}/cancelamento", async (Guid id, CancelamentoRequest request, CancelarNfeHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new CancelarNfeCommand(id, request.Justificativa), ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível cancelar a nota.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Cancela uma nota autorizada (evento 110111) dentro de 24 h da autorização. Síncrono: a resposta traz o cStat do evento (135 = registrado).")
        .WithDescription("Se a SEFAZ não responder, o evento fica Pendente e a mesma chamada, repetida, o conclui sem duplicar.");

        group.MapPost("/{id:guid}/carta-correcao", async (Guid id, CartaCorrecaoRequest request, EnviarCartaCorrecaoHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new CartaCorrecaoCommand(id, request.Correcao), ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível enviar a carta de correção.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Carta de correção (evento 110110). Cada carta nova substitui as anteriores; até 20 por nota.");

        group.MapGet("/{id:guid}/eventos", async (Guid id, ListarEventosHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Nota não encontrada.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Eventos da nota (cancelamento e cartas de correção), com status e protocolo.");

        group.MapGet("/{id:guid}/eventos/{eventoId:guid}/xml", async (Guid id, Guid eventoId, GetEventoXmlHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, eventoId, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "XML indisponível.")
                : Results.Text(XmlDeclaration + result.Value, "application/xml", System.Text.Encoding.UTF8);
        })
        .WithSummary("XML do evento (procEventoNFe quando registrado).");

        group.MapPost("/{id:guid}/reenviar", async (Guid id, EmissaoNfeRequest? pedido, ReenviarNfeHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new ReenviarNfeCommand(id, pedido), ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível reenviar a nota.")
                : Results.Accepted($"/api/v1/nfe/{id}", result.Value);
        })
        .WithSummary("Reenvia uma nota rejeitada ou com falha (corpo opcional: pedido corrigido; número e série mantidos). Pendente de consulta: sem corpo, antecipa a próxima tentativa.");

        return group;
    }
}
