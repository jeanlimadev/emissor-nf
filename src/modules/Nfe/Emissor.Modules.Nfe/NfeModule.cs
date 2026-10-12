using Emissor.BuildingBlocks;
using Emissor.Contracts.Nfe;
using Emissor.Modules.Nfe.Application.Consulta;
using Emissor.Modules.Nfe.Application.Emissao;
using Emissor.Modules.Nfe.Application.Eventos;
using Emissor.Modules.Nfe.Application.Ports;
using Emissor.Modules.Nfe.Application.Webhooks;
using Emissor.Modules.Nfe.Infrastructure.Assinatura;
using Emissor.Modules.Nfe.Infrastructure.Certificados;
using Emissor.Modules.Nfe.Infrastructure.Danfe;
using Emissor.Modules.Nfe.Infrastructure.Fila;
using Emissor.Modules.Nfe.Infrastructure.Persistence;
using Emissor.Modules.Nfe.Infrastructure.Repositories;
using Emissor.Modules.Nfe.Infrastructure.Sefaz;
using Emissor.Modules.Nfe.Infrastructure.Webhooks;
using Emissor.Modules.Nfe.Infrastructure.Xml;
using Emissor.Modules.Nfe.Presentation;
using Emissor.Modules.Tenancy.Presentation;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Emissor.Modules.Nfe;

/// <summary>
/// Emissao, consulta e eventos da NF-e. Fase 1: pedido -> XML -> assinatura
/// -> XSD (previa). Fase 2: fila no Postgres, worker, transmissao ao SVRS e
/// maquina de estados da nota.
/// </summary>
public static class NfeModule
{
    public static IServiceCollection AddNfeModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Mensagens padrao do FluentValidation ("must not be empty") em portugues.
        ValidatorOptions.Global.LanguageManager.Culture = new System.Globalization.CultureInfo("pt-BR");

        services.AddDbContext<NfeDbContext>((serviceProvider, options) =>
            options.UseNpgsql(
                serviceProvider.GetRequiredService<IConfiguration>().GetConnectionString(DatabaseConnection.Name),
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations", NfeDbContext.Schema)));

        services.Configure<SefazOptions>(configuration.GetSection(SefazOptions.Secao));
        services.Configure<WorkerOptions>(configuration.GetSection(WorkerOptions.Secao));
        services.Configure<ResponsavelTecnicoOptions>(configuration.GetSection(ResponsavelTecnicoOptions.Secao));

        services.AddSingleton<IValidator<EmissaoNfeRequest>, EmissaoNfeRequestValidator>();
        services.AddSingleton<INfeXmlBuilder, DfeNetNfeXmlBuilder>();
        services.AddSingleton<INfeXsdValidator, NfeXsdValidator>();
        services.AddSingleton<INfeAssinador, XmlDsigNfeAssinador>();
        services.AddSingleton<IGeradorCodigoNumerico, GeradorCodigoNumericoAleatorio>();
        services.AddSingleton<ICertificadoAbridor, CertificadoAbridor>();
        services.AddSingleton<ISefazGateway, SoapSefazGateway>();
        services.AddSingleton<IDanfeRenderer, QuestPdfDanfeRenderer>();

        services.AddScoped<INfeRepository, EfNfeRepository>();
        services.AddScoped<INfeFila, EfNfeFila>();
        services.AddScoped<IWebhookRepository, EfWebhookRepository>();
        services.AddScoped<IEventoRepository, EfEventoRepository>();
        services.AddScoped<IWebhookFila, EfWebhookFila>();

        // Entrega de webhook: timeout curto e sem redirecionamento (a URL configurada e a que recebe).
        services.AddHttpClient(HttpWebhookEntregador.NomeCliente, client => client.Timeout = TimeSpan.FromSeconds(10))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false, PooledConnectionLifetime = TimeSpan.FromMinutes(5) });
        services.AddSingleton<IWebhookEntregador, HttpWebhookEntregador>();

        services.AddScoped<MontadorDeNfe>();
        services.AddScoped<GerarPreviaNfeHandler>();
        services.AddScoped<EmitirNfeHandler>();
        services.AddScoped<ProcessarNfeHandler>();
        services.AddScoped<GetNfeHandler>();
        services.AddScoped<GetNfeXmlHandler>();
        services.AddScoped<ListNfesHandler>();
        services.AddScoped<AguardarNfeHandler>();
        services.AddScoped<ReenviarNfeHandler>();
        services.AddScoped<ConsultarStatusServicoHandler>();
        services.AddScoped<GetDanfeHandler>();
        services.AddScoped<AgendadorDeWebhook>();
        services.AddScoped<ConfigurarWebhookHandler>();
        services.AddScoped<GetWebhookHandler>();
        services.AddScoped<RemoverWebhookHandler>();
        services.AddScoped<ListarEntregasHandler>();
        services.AddScoped<TestarWebhookHandler>();
        services.AddScoped<EntregarWebhookHandler>();
        services.AddScoped<TransmissorDeEventos>();
        services.AddScoped<CancelarNfeHandler>();
        services.AddScoped<EnviarCartaCorrecaoHandler>();
        services.AddScoped<ListarEventosHandler>();
        services.AddScoped<GetEventoXmlHandler>();
        services.AddScoped<InutilizarHandler>();
        services.AddScoped<ListarInutilizacoesHandler>();
        services.AddScoped<GetInutilizacaoXmlHandler>();

        services.AddHostedService<NfeWorker>();
        services.AddHostedService<WebhookWorker>();
        services.AddHostedService<CertificadoVencimentoWorker>();

        return services;
    }

    public static IEndpointRouteBuilder MapNfeModule(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapGroup("/api/v1/emitentes/{id:guid}")
            .WithTags("NF-e")
            .RequireAuthorization(AuthPolicies.Tenant)
            .MapNfePorEmitente();

        endpoints
            .MapGroup("/api/v1/nfe")
            .WithTags("NF-e")
            .RequireAuthorization(AuthPolicies.Tenant)
            .MapNotas();

        endpoints
            .MapGroup("/api/v1/webhook")
            .WithTags("Webhook")
            .RequireAuthorization(AuthPolicies.Tenant)
            .MapWebhook();

        return endpoints;
    }

    public static async Task MigrateNfeAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NfeDbContext>().Database.MigrateAsync(cancellationToken);
    }
}
