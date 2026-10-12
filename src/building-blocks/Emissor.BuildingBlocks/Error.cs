namespace Emissor.BuildingBlocks;

/// <summary>
/// A natureza do erro, que o Presentation traduz em codigo HTTP. O handler diz
/// o que aconteceu; quem decide se isso e 404 ou 409 e um lugar so
/// (<see cref="ProblemResults"/>), e nao cada endpoint.
/// </summary>
public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unauthorized,
    Unavailable
}

/// <param name="Code">Codigo estavel, em ingles e com pontos (<c>emitente.nao_encontrado</c>), para o cliente tratar por codigo e nao por texto.</param>
/// <param name="Message">A explicacao, em portugues, para quem le a resposta.</param>
public sealed record Error(string Code, string Message, ErrorKind Kind = ErrorKind.Validation)
{
    /// <summary>
    /// Detalhes por campo, quando a validacao achou varios problemas de uma
    /// vez (campo -> mensagens). Vai na extensao <c>errors</c> do ProblemDetails,
    /// no mesmo formato do <c>ValidationProblemDetails</c> do ASP.NET.
    /// </summary>
    public IReadOnlyDictionary<string, string[]>? Details { get; init; }

    public Error ComDetalhes(IReadOnlyDictionary<string, string[]> details) => this with { Details = details };

    public static Error Validation(string code, string message) => new(code, message, ErrorKind.Validation);
    public static Error NotFound(string code, string message) => new(code, message, ErrorKind.NotFound);
    public static Error Conflict(string code, string message) => new(code, message, ErrorKind.Conflict);
    public static Error Forbidden(string code, string message) => new(code, message, ErrorKind.Forbidden);
    public static Error Unauthorized(string code, string message) => new(code, message, ErrorKind.Unauthorized);
    public static Error Unavailable(string code, string message) => new(code, message, ErrorKind.Unavailable);
}
