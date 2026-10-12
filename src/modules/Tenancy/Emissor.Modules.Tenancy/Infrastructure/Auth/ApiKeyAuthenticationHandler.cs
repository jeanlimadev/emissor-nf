using System.Security.Claims;
using System.Text.Encodings.Web;
using Emissor.Modules.Tenancy.Application.ApiKeys.Authenticate;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Emissor.Modules.Tenancy.Infrastructure.Auth;

/// <summary>
/// Esquema <c>ApiKey</c>: le o cabecalho <c>X-Api-Key</c>, resolve o tenant e
/// monta o principal com as claims que o <see cref="HttpCurrentTenant"/> le.
/// Sem cabecalho, nao autentica (e a politica da rota que recusa).
/// </summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AuthenticateApiKeyHandler authenticate)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || values.Count == 0)
            return AuthenticateResult.NoResult();

        var result = await authenticate.HandleAsync(values[0], Context.RequestAborted);

        if (result.IsFailure)
            return AuthenticateResult.Fail(result.Error!.Message);

        var tenant = result.Value!;
        var identity = new ClaimsIdentity(
        [
            new Claim(TenantClaims.TenantId, tenant.TenantId.ToString()),
            new Claim(TenantClaims.TenantNome, tenant.TenantNome),
            new Claim(TenantClaims.ApiKeyId, tenant.ApiKeyId.ToString())
        ], SchemeName);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}

public static class TenantClaims
{
    public const string TenantId = "tenant_id";
    public const string TenantNome = "tenant_nome";
    public const string ApiKeyId = "api_key_id";
}
