namespace Emissor.BuildingBlocks;

/// <summary>
/// Resultado de um caso de uso sem valor de retorno. Falha esperada (validacao,
/// nao encontrado, conflito) vem aqui; excecao e so para o inesperado.
/// </summary>
public sealed class Result
{
    private Result(Error? error) => Error = error;

    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    public static Result Success() => new(null);
    public static Result Failure(Error error) => new(error);

    public Result<TOther> FailAs<TOther>() => Result<TOther>.Failure(Error ?? throw new InvalidOperationException("Resultado sem erro."));
}

public sealed class Result<T>
{
    private Result(T? value, Error? error)
    {
        Value = value;
        Error = error;
    }

    public T? Value { get; }
    public Error? Error { get; }
    public bool IsSuccess => Error is null;
    public bool IsFailure => !IsSuccess;

    public static Result<T> Success(T value) => new(value, null);
    public static Result<T> Failure(Error error) => new(default, error);

    /// <summary>Converte a falha para um resultado de outro tipo, sem repetir o <c>Failure(result.Error!)</c> em cada handler.</summary>
    public Result<TOther> FailAs<TOther>() => Result<TOther>.Failure(Error ?? throw new InvalidOperationException("Resultado sem erro."));
}
