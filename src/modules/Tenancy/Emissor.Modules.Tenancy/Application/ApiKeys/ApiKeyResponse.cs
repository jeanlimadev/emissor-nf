using Emissor.Modules.Tenancy.Domain;

namespace Emissor.Modules.Tenancy.Application.ApiKeys;

public sealed record ApiKeyResponse(
    Guid Id,
    string Nome,
    string Prefixo,
    bool Ativa,
    DateTimeOffset CriadaEm,
    DateTimeOffset? RevogadaEm,
    DateTimeOffset? UltimoUsoEm)
{
    public static ApiKeyResponse From(ApiKey key) =>
        new(key.Id, key.Nome, key.Prefixo, key.Ativa, key.CriadaEm, key.RevogadaEm, key.UltimoUsoEm);
}

/// <summary>Resposta da criacao: a unica vez em que <see cref="Chave"/> aparece em texto.</summary>
public sealed record ApiKeyCreatedResponse(Guid Id, string Nome, string Prefixo, string Chave, DateTimeOffset CriadaEm);
