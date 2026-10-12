using Microsoft.AspNetCore.Http;

namespace Emissor.BuildingBlocks;

/// <summary>
/// Traduz um <see cref="Error"/> em <c>ProblemDetails</c>. O codigo interno
/// vai na extensao <c>code</c>, para o cliente tratar por codigo e nao por texto.
/// </summary>
public static class ProblemResults
{
    public static IResult FromError(Error error, string title)
    {
        var extensions = new Dictionary<string, object?>
        {
            ["code"] = error.Code
        };

        if (error.Details is { Count: > 0 })
            extensions["errors"] = error.Details;

        return Results.Problem(
            title: title,
            detail: error.Message,
            statusCode: StatusCodeFor(error.Kind),
            extensions: extensions);
    }

    public static int StatusCodeFor(ErrorKind kind) => kind switch
    {
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Unavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status400BadRequest
    };
}
