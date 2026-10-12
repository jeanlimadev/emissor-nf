using Emissor.Api.Infrastructure;
using Emissor.BuildingBlocks;
using Emissor.Modules.Emitentes;
using Emissor.Modules.Nfe;
using Emissor.Modules.Tenancy;
using Emissor.Modules.Tenancy.Infrastructure.Auth;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;
using System.Threading.RateLimiting;

// Antes de qualquer criptografia: a NF-e exige RSA-SHA1 e Fedora/RHEL bloqueiam SHA-1 por politica (ver OpenSslSha1).
OpenSslSha1.PermitirSeNecessario();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<RequisicaoInvalidaExceptionHandler>();
// Corpo invalido vira excecao em todo ambiente (nao so em Development), para o handler acima responder 400 com codigo.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.CustomSchemaIds(type => type.FullName?.Replace('+', '.') ?? type.Name);
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Emissor NF-e",
        Version = "v1",
        Description = "Serviço de emissão de NF-e (modelo 55) para vários emitentes e vários sistemas clientes."
    });
    options.AddSecurityDefinition(ApiKeyAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
    {
        Name = ApiKeyAuthenticationHandler.HeaderName,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Chave de API do tenant (sistema cliente)."
    });
    options.AddSecurityDefinition(AdminTokenAuthenticationHandler.SchemeName, new OpenApiSecurityScheme
    {
        Name = AdminTokenAuthenticationHandler.HeaderName,
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Description = "Token de administração do serviço."
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference(ApiKeyAuthenticationHandler.SchemeName, document, null)] = [],
        [new OpenApiSecuritySchemeReference(AdminTokenAuthenticationHandler.SchemeName, document, null)] = []
    });
});

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<CertificadosHealthCheck>("certificados");

// Limite por chave de API (janela fixa de 1 minuto). Sem chave (admin, health), por endereco IP.
var limite = builder.Configuration.GetSection(RateLimitOptions.Secao).Get<RateLimitOptions>() ?? new RateLimitOptions();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Limite de requisições atingido.",
            Detail = $"Esta chave pode fazer até {limite.PorMinuto} requisições por minuto. Aguarde e tente de novo.",
            Extensions = { ["code"] = "requisicao.limite_atingido" }
        }, ct);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var chave = context.User.FindFirst(TenantClaims.ApiKeyId)?.Value
            ?? context.Connection.RemoteIpAddress?.ToString()
            ?? "anonimo";

        return RateLimitPartition.GetFixedWindowLimiter(chave, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limite.PorMinuto,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

builder.Services
    .AddTenancyModule()
    .AddEmitentesModule()
    .AddNfeModule(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// O documento OpenAPI fica publicado em todo ambiente (o ERP gera o cliente a partir dele); a UI so em desenvolvimento.
app.UseSwagger();

if (app.Environment.IsDevelopment())
    app.UseSwaggerUI();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health");
app.MapTenancyModule();
app.MapEmitentesModule();
app.MapNfeModule();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
    await app.Services.MigrateAllAsync(CancellationToken.None);

await app.RunAsync();

/// <summary>Exposto para o <c>WebApplicationFactory</c> dos testes de integracao.</summary>
public partial class Program;
