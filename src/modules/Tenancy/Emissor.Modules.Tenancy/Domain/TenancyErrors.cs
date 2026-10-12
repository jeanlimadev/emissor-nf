using Emissor.BuildingBlocks;

namespace Emissor.Modules.Tenancy.Domain;

public static class TenancyErrors
{
    public static readonly Error NomeInvalido =
        Error.Validation("tenant.nome_invalido", $"Informe o nome do tenant com até {Tenant.NomeMaxLength} caracteres.");

    public static readonly Error TenantNaoEncontrado =
        Error.NotFound("tenant.nao_encontrado", "Tenant não encontrado.");

    public static readonly Error TenantInativo =
        Error.Forbidden("tenant.inativo", "O tenant está desativado.");

    public static readonly Error NomeApiKeyInvalido =
        Error.Validation("api_key.nome_invalido", $"Informe o nome da chave com até {ApiKey.NomeMaxLength} caracteres.");

    public static readonly Error ApiKeyNaoEncontrada =
        Error.NotFound("api_key.nao_encontrada", "Chave de API não encontrada.");

    public static readonly Error ApiKeyInvalida =
        Error.Unauthorized("api_key.invalida", "Chave de API inválida ou revogada.");
}
