using Emissor.BuildingBlocks;
using Emissor.Modules.Tenancy.Application.ApiKeys.Authenticate;
using Emissor.Modules.Tenancy.Application.ApiKeys.Create;
using Emissor.Modules.Tenancy.Application.ApiKeys.List;
using Emissor.Modules.Tenancy.Application.ApiKeys.Revoke;
using Emissor.Modules.Tenancy.Application.Ports;
using Emissor.Modules.Tenancy.Application.Tenants.Create;
using Emissor.Modules.Tenancy.Application.Tenants.List;
using Emissor.Modules.Tenancy.Infrastructure.Auth;
using Emissor.Modules.Tenancy.Infrastructure.Persistence;
using Emissor.Modules.Tenancy.Infrastructure.Repositories;
using Emissor.Modules.Tenancy.Presentation;
using Emissor.Modules.Tenancy.Presentation.Admin;
using Emissor.Modules.Tenancy.Presentation.Me;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Emissor.Modules.Tenancy;

/// <summary>
/// Quem e quem: tenants (sistemas clientes), suas API keys e a autenticacao
/// das requisicoes. Os outros modulos so conhecem <see cref="ICurrentTenant"/>.
/// </summary>
public static class TenancyModule
{
    public static IServiceCollection AddTenancyModule(this IServiceCollection services)
    {
        services.AddDbContext<TenancyDbContext>((serviceProvider, options) =>
            options.UseNpgsql(
                serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(DatabaseConnection.Name),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", TenancyDbContext.Schema)));

        services.AddScoped<ITenantRepository, EfTenantRepository>();
        services.AddScoped<IApiKeyRepository, EfApiKeyRepository>();
        services.AddScoped<CreateTenantHandler>();
        services.AddScoped<ListTenantsHandler>();
        services.AddScoped<CreateApiKeyHandler>();
        services.AddScoped<ListApiKeysHandler>();
        services.AddScoped<RevokeApiKeyHandler>();
        services.AddScoped<AuthenticateApiKeyHandler>();

        // O tenant corrente: das claims quando ha requisicao HTTP autenticada por
        // API key; senao, o tenant ambiente que o worker amarra ao abrir o escopo.
        services.AddHttpContextAccessor();
        services.AddScoped<AmbientTenant>();
        services.AddScoped<ICurrentTenant>(serviceProvider =>
        {
            var httpContext = serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;

            return httpContext is not null && HttpCurrentTenant.HasTenant(httpContext.User)
                ? new HttpCurrentTenant(httpContext.User)
                : serviceProvider.GetRequiredService<AmbientTenant>();
        });

        services
            .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationHandler.SchemeName, null)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AdminTokenAuthenticationHandler>(
                AdminTokenAuthenticationHandler.SchemeName, null);

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthPolicies.Tenant, policy => policy
                .AddAuthenticationSchemes(ApiKeyAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireClaim(TenantClaims.TenantId))
            .AddPolicy(AuthPolicies.Admin, policy => policy
                .AddAuthenticationSchemes(AdminTokenAuthenticationHandler.SchemeName)
                .RequireAuthenticatedUser()
                .RequireRole(AdminTokenAuthenticationHandler.RoleClaim));

        return services;
    }

    public static IEndpointRouteBuilder MapTenancyModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGroup("/api/v1/admin")
            .WithTags("Administração")
            .RequireAuthorization(AuthPolicies.Admin)
            .MapTenantAdmin();

        endpoints
            .MapGroup("/api/v1")
            .WithTags("Tenant")
            .RequireAuthorization(AuthPolicies.Tenant)
            .MapMe();

        return endpoints;
    }

    public static async Task MigrateTenancyAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        // await aqui, e nao "return": o escopo (e o DbContext) tem que viver ate a migration acabar.
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<TenancyDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
