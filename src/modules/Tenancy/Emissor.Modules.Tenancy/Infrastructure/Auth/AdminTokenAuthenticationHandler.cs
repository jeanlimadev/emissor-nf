using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Emissor.Modules.Tenancy.Infrastructure.Auth;

/// <summary>
/// Esquema <c>Admin</c>: um token unico de operacao, vindo da configuracao
/// (<c>Admin:Token</c>, em producao via variavel de ambiente), para criar
/// tenants e chaves. E a porta de quem opera o servico, nao dos sistemas
/// clientes. Sem token configurado, a administracao fica fechada.
/// </summary>
public sealed class AdminTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IConfiguration configuration)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Admin";
    public const string HeaderName = "X-Admin-Token";
    public const string ConfigurationKey = "Admin:Token";
    public const string RoleClaim = "admin";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var values) || values.Count == 0)
            return Task.FromResult(AuthenticateResult.NoResult());

        var configured = configuration[ConfigurationKey];

        if (string.IsNullOrWhiteSpace(configured))
            return Task.FromResult(AuthenticateResult.Fail("Administração desabilitada: token não configurado."));

        var presented = Encoding.UTF8.GetBytes(values[0] ?? string.Empty);
        var expected = Encoding.UTF8.GetBytes(configured);

        if (!CryptographicOperations.FixedTimeEquals(presented, expected))
            return Task.FromResult(AuthenticateResult.Fail("Token de administração inválido."));

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Role, RoleClaim)], SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }
}
