using Emissor.Modules.Emitentes;
using Emissor.Modules.Nfe;
using Emissor.Modules.Tenancy;

namespace Emissor.Api.Infrastructure;

/// <summary>
/// Aplica as migrations de todos os modulos, em ordem de dependencia. Chamado
/// na subida quando <c>Database:MigrateOnStartup</c> esta ligado (ambiente de
/// desenvolvimento e deploy em container) e pelos testes de integracao.
/// </summary>
public static class DatabaseMigrations
{
    public static async Task MigrateAllAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await services.MigrateTenancyAsync(cancellationToken);
        await services.MigrateEmitentesAsync(cancellationToken);
        await services.MigrateNfeAsync(cancellationToken);
    }
}
