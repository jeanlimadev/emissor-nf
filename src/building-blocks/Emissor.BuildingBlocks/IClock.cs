namespace Emissor.BuildingBlocks;

/// <summary>Relogio injetavel: o dominio nao chama <c>DateTimeOffset.UtcNow</c> direto, para os testes controlarem o tempo.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
