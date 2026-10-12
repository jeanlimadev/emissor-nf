using Emissor.BuildingBlocks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Emissor.Api.Infrastructure;

/// <summary>Abre uma conexao e executa <c>SELECT 1</c>. Sem o banco, o servico nao emite nada.</summary>
internal sealed class DatabaseHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(configuration.GetConnectionString(DatabaseConnection.Name));
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(SecretRedactor.Redact(exception.Message));
        }
    }
}
