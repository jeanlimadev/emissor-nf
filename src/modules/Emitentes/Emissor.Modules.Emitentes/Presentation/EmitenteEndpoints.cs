using Emissor.BuildingBlocks;
using Emissor.Contracts.Emitentes;
using Emissor.Modules.Emitentes.Application.Certificados;
using Emissor.Modules.Emitentes.Application.Emitentes;
using Emissor.Modules.Emitentes.Application.Series;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Emissor.Modules.Emitentes.Presentation;

public sealed record UpdateSerieRequest(long? ProximoNumero, bool? Ativa);

internal static class EmitenteEndpoints
{
    /// <summary>Limite do pfx mais folga para o envelope multipart.</summary>
    private const long MaxCertificadoRequestBytes = UploadCertificadoHandler.PfxMaxBytes + 16 * 1024;

    internal static RouteGroupBuilder MapEmitentes(this RouteGroupBuilder group)
    {
        group.MapPost("/", async (EmitenteRequest request, CreateEmitenteHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(request, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível cadastrar o emitente.")
                : Results.Created($"/api/v1/emitentes/{result.Value!.Id}", result.Value);
        })
        .WithSummary("Cadastra um emitente (CPF de produtor rural com IE ou CNPJ).");

        group.MapGet("/", async (int? page, int? pageSize, bool? ativo, ListEmitentesHandler handler, CancellationToken ct) =>
        {
            if (!PageRequest.TryCreate(page, pageSize, out var request, out var pageSizeInvalid))
                return ProblemResults.FromError(
                    pageSizeInvalid ? PageRequest.InvalidPageSize : PageRequest.InvalidPage,
                    "Parâmetros de paginação inválidos.");

            return Results.Ok(await handler.HandleAsync(request, ativo, ct));
        })
        .WithSummary("Lista os emitentes do tenant.");

        group.MapGet("/{id:guid}", async (Guid id, GetEmitenteHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Emitente não encontrado.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Consulta um emitente.");

        group.MapPut("/{id:guid}", async (Guid id, EmitenteRequest request, UpdateEmitenteHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, request, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível atualizar o emitente.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Atualiza o cadastro do emitente.");

        group.MapPost("/{id:guid}/desativar", async (Guid id, SetEmitenteAtivoHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, false, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível desativar o emitente.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Desativa o emitente: ele deixa de emitir, mas o histórico fica.");

        group.MapPost("/{id:guid}/reativar", async (Guid id, SetEmitenteAtivoHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, true, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível reativar o emitente.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Reativa o emitente.");

        group.MapPost("/{id:guid}/contingencia", async (Guid id, ContingenciaRequest request, SetContingenciaHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, request.Justificativa, true, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível ativar a contingência.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Liga a contingência SVC-AN: as próximas notas saem com tpEmis 6 e vão para a Sefaz Virtual de Contingência do Ambiente Nacional.")
        .WithDescription("Use quando o SVRS estiver fora (status 108/109 ou falha de rede persistente). A justificativa vai no XML de cada nota (xJust).");

        group.MapDelete("/{id:guid}/contingencia", async (Guid id, SetContingenciaHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, null, false, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível desligar a contingência.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Desliga a contingência: volta ao autorizador normal.");

        group.MapPut("/{id:guid}/certificado", async (
            Guid id,
            IFormFile arquivo,
            [FromForm] string senha,
            UploadCertificadoHandler handler,
            CancellationToken ct) =>
        {
            if (arquivo.Length > UploadCertificadoHandler.PfxMaxBytes)
                return ProblemResults.FromError(
                    Domain.EmitenteErrors.CertificadoMuitoGrande,
                    "Não foi possível guardar o certificado.");

            using var buffer = new MemoryStream();
            await arquivo.CopyToAsync(buffer, ct);

            var result = await handler.HandleAsync(new UploadCertificadoCommand(id, buffer.ToArray(), senha ?? string.Empty), ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível guardar o certificado.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Envia (ou troca) o certificado A1 do emitente: multipart com o campo `arquivo` (.pfx) e `senha`.")
        // A API e autenticada por cabecalho, sem cookie de sessao: nao ha antiforgery a validar.
        .DisableAntiforgery()
        .WithMetadata(new RequestSizeLimitAttribute(MaxCertificadoRequestBytes))
        .WithMetadata(new RequestFormLimitsAttribute { MultipartBodyLengthLimit = MaxCertificadoRequestBytes });

        group.MapGet("/{id:guid}/certificado", async (Guid id, GetCertificadoHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Certificado não encontrado.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Metadados do certificado (titular, validade). O arquivo nunca sai do serviço.");

        group.MapDelete("/{id:guid}/certificado", async (Guid id, RemoveCertificadoHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível remover o certificado.")
                : Results.NoContent();
        })
        .WithSummary("Remove o certificado do emitente.");

        group.MapPost("/{id:guid}/series", async (Guid id, SerieRequest request, CreateSerieHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, request, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível criar a série.")
                : Results.Created($"/api/v1/emitentes/{id}/series/{result.Value!.Id}", result.Value);
        })
        .WithSummary("Cria uma série de numeração (modelo 55) para o emitente.");

        group.MapGet("/{id:guid}/series", async (Guid id, ListSeriesHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(id, ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Emitente não encontrado.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Lista as séries do emitente.");

        group.MapPut("/{id:guid}/series/{serieId:guid}", async (
            Guid id,
            Guid serieId,
            UpdateSerieRequest request,
            UpdateSerieHandler handler,
            CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(new UpdateSerieCommand(id, serieId, request.ProximoNumero, request.Ativa), ct);

            return result.IsFailure
                ? ProblemResults.FromError(result.Error!, "Não foi possível atualizar a série.")
                : Results.Ok(result.Value);
        })
        .WithSummary("Ajusta o próximo número (nunca para trás) ou ativa/desativa a série.");

        return group;
    }
}
