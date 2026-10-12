namespace Emissor.Api.Infrastructure;

/// <summary>Secao <c>RateLimit</c>: requisicoes por minuto por chave de API (ou por IP quando nao ha chave).</summary>
public sealed class RateLimitOptions
{
    public const string Secao = "RateLimit";

    public int PorMinuto { get; set; } = 300;
}
