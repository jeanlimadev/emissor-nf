using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Emissor.Api.Infrastructure;

/// <summary>
/// Corpo que nao e JSON, JSON malformado ou campo com tipo errado chegam
/// como <see cref="BadHttpRequestException"/>. Em Development o ASP.NET lanca
/// em vez de responder 400, e sem isto o cliente veria um 500 enganoso. Aqui
/// vira ProblemDetails com o status da propria excecao e um codigo estavel.
/// </summary>
public sealed class RequisicaoInvalidaExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public const string Code = "requisicao.corpo_invalido";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
            return false;

        httpContext.Response.StatusCode = badRequest.StatusCode;

        var detalhe = Detalhe(badRequest);

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Title = "O corpo da requisição não pôde ser lido.",
                Detail = detalhe,
                Status = badRequest.StatusCode,
                Extensions = { ["code"] = Code }
            }
        });
    }

    /// <summary>
    /// A mensagem do System.Text.Json diz onde o JSON quebrou (caminho, linha,
    /// coluna) e nao vaza segredo, mas vem em ingles e sem dizer o que fazer.
    /// Aqui ela ganha um cabecalho em portugues e a dica mais comum: aspas
    /// dentro do texto precisam de barra invertida.
    /// </summary>
    private static string Detalhe(BadHttpRequestException excecao)
    {
        var original = excecao.InnerException?.Message ?? excecao.Message;

        if (excecao.InnerException is System.Text.Json.JsonException)
        {
            return "O corpo não é um JSON válido. Confira aspas e vírgulas: uma aspa dentro de um texto deve ser escrita como \\\" (barra invertida e aspa). "
                + "Detalhe do parser: " + original;
        }

        return original;
    }
}
