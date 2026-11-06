using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Transcendence.Api.Data;

/// <summary>GET /health answers Healthy only when PostgreSQL answers a query.</summary>
public sealed class DatabaseHealthCheck(NpgsqlDataSource db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var command = db.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("database unreachable", error);
        }
    }
}
