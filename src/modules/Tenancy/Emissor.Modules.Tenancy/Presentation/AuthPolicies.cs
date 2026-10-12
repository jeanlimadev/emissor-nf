namespace Emissor.Modules.Tenancy.Presentation;

/// <summary>Nomes das politicas de autorizacao que o host registra e os modulos exigem nas rotas.</summary>
public static class AuthPolicies
{
    /// <summary>Requisicao de um sistema cliente autenticado por API key.</summary>
    public const string Tenant = "Tenant";

    /// <summary>Requisicao de quem opera o servico (token de administracao).</summary>
    public const string Admin = "Admin";
}
