using Emissor.BuildingBlocks;
using Emissor.Modules.Emitentes.Application.Certificados;
using Emissor.Modules.Emitentes.Application.Emitentes;
using Emissor.Modules.Emitentes.Application.Ports;
using Emissor.Modules.Emitentes.Application.Series;
using Emissor.Modules.Emitentes.Infrastructure.Certificados;
using Emissor.Modules.Emitentes.Infrastructure.Persistence;
using Emissor.Modules.Emitentes.Infrastructure.Repositories;
using Emissor.Modules.Emitentes.Presentation;
using Emissor.Modules.Tenancy.Presentation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Emissor.Modules.Emitentes;

/// <summary>
/// Cadastro de quem emite: dados do grupo <c>emit</c>, certificado A1
/// criptografado e series de numeracao. Tudo por tenant.
/// </summary>
public static class EmitentesModule
{
    public const string ChaveMestraConfigurationKey = "Certificados:ChaveMestra";

    public static IServiceCollection AddEmitentesModule(this IServiceCollection services)
    {
        services.AddDbContext<EmitentesDbContext>((serviceProvider, options) =>
            options.UseNpgsql(
                serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(DatabaseConnection.Name),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", EmitentesDbContext.Schema)));

        services.AddSingleton<ICertificadoProtector>(serviceProvider =>
            AesGcmCertificadoProtector.FromBase64(
                serviceProvider.GetRequiredService<IConfiguration>()[ChaveMestraConfigurationKey]));
        services.AddSingleton<ICertificadoInspector, X509CertificadoInspector>();

        services.AddScoped<IEmitenteRepository, EfEmitenteRepository>();
        services.AddScoped<ISerieRepository, EfSerieRepository>();

        services.AddScoped<CreateEmitenteHandler>();
        services.AddScoped<UpdateEmitenteHandler>();
        services.AddScoped<GetEmitenteHandler>();
        services.AddScoped<ListEmitentesHandler>();
        services.AddScoped<SetEmitenteAtivoHandler>();
        services.AddScoped<SetContingenciaHandler>();
        services.AddScoped<UploadCertificadoHandler>();
        services.AddScoped<GetCertificadoHandler>();
        services.AddScoped<RemoveCertificadoHandler>();
        services.AddScoped<CreateSerieHandler>();
        services.AddScoped<ListSeriesHandler>();
        services.AddScoped<UpdateSerieHandler>();

        return services;
    }

    public static IEndpointRouteBuilder MapEmitentesModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGroup("/api/v1/emitentes")
            .WithTags("Emitentes")
            .RequireAuthorization(AuthPolicies.Tenant)
            .MapEmitentes();

        return endpoints;
    }

    public static async Task MigrateEmitentesAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        // await aqui, e nao "return": o escopo (e o DbContext) tem que viver ate a migration acabar.
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<EmitentesDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
