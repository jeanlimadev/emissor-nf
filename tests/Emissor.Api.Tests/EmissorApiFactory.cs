using System.Security.Cryptography;
using Emissor.Api.Infrastructure;
using Emissor.Modules.Nfe.Application.Ports;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Emissor.Api.Tests;

/// <summary>
/// Sobe a API inteira contra um Postgres de verdade (Testcontainers), com as
/// migrations aplicadas. Um container por assembly de teste; cada classe usa
/// tenants proprios, entao os testes nao se enxergam.
/// </summary>
public sealed class EmissorApiFactory : WebApplicationFactory<Program>
{
    public const string AdminToken = "token-admin-de-teste";

    private static readonly PostgreSqlContainer Postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("emissor_testes")
        .WithUsername("emissor")
        .WithPassword("emissor")
        .Build();

    private static readonly string ChaveMestra = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static async Task StartAsync()
    {
        await Postgres.StartAsync();

        using var factory = new EmissorApiFactory();
        await factory.Services.MigrateAllAsync(CancellationToken.None);
    }

    public static Task StopAsync() => Postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Emissor", Postgres.GetConnectionString());
        builder.UseSetting("Admin:Token", AdminToken);
        builder.UseSetting("Certificados:ChaveMestra", ChaveMestra);
        builder.UseSetting("Database:MigrateOnStartup", "false");
        // Worker rapido e espera curta entre tentativas: os testes nao podem esperar 30 s.
        builder.UseSetting("Nfe:Worker:IntervaloSegundos", "0.2");
        builder.UseSetting("Nfe:Worker:EsperaBaseSegundos", "0.5");
        builder.UseSetting("Nfe:Worker:EsperaMaximaSegundos", "2");
        builder.UseSetting("Nfe:Worker:PosseSegundos", "5");
        builder.UseSetting("Nfe:Worker:VerificacaoCertificadosHoras", "0.002"); // ~7 s

        // A SEFAZ de verdade fica fora dos testes automatizados; a falsa decide pelo codigo do produto.
        builder.ConfigureTestServices(services => services.AddSingleton<ISefazGateway, FakeSefazGateway>());
    }
}

[TestClass]
public static class AssemblySetup
{
    [AssemblyInitialize]
    public static Task Up(TestContext _) => EmissorApiFactory.StartAsync();

    [AssemblyCleanup]
    public static Task Down() => EmissorApiFactory.StopAsync();
}
