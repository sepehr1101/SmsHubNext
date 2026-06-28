using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmsHubNext.Shared.Database;

/// <summary>
/// Readiness probe for the database: opens a connection and runs <c>SELECT 1</c>.
/// Surfaced at <c>/health</c> so IIS and operators see SQL Server connectivity.
/// </summary>
public sealed class SqlServerHealthCheck : IHealthCheck
{
    private readonly Db _db;

    public SqlServerHealthCheck(Db db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
            await connection.ExecuteScalarAsync<int>(
                new CommandDefinition("SELECT 1", cancellationToken: cancellationToken));

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("SQL Server is not reachable.", ex);
        }
    }
}
