using System.Diagnostics;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmsHubNext.Shared.Database;

/// <summary>
/// Readiness probe for connectivity, database availability/writeability, response time, and clock
/// skew. Surfaced at <c>/health</c> and <c>/health/ready</c> for IIS and operators.
/// </summary>
public sealed class SqlServerHealthCheck : IHealthCheck
{
    private static readonly TimeSpan DegradedLatency = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DegradedClockSkew = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan UnhealthyClockSkew = TimeSpan.FromMinutes(2);

    private const string ReadinessSql =
        """
        SELECT
            CONVERT(VARCHAR(60), DATABASEPROPERTYEX(DB_NAME(), 'Status')) AS DatabaseStatus,
            CONVERT(VARCHAR(60), DATABASEPROPERTYEX(DB_NAME(), 'Updateability')) AS Updateability,
            SYSUTCDATETIME() AS ServerUtc;
        """;

    private readonly Db _db;
    private readonly TimeProvider _clock;

    public SqlServerHealthCheck(Db db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            await using SqlConnection connection = await _db.OpenConnectionAsync(cancellationToken);
            SqlServerReadinessRow row = await connection.QuerySingleAsync<SqlServerReadinessRow>(
                new CommandDefinition(
                    ReadinessSql,
                    commandTimeout: 3,
                    cancellationToken: cancellationToken));
            stopwatch.Stop();

            DateTime applicationUtc = _clock.GetUtcNow().UtcDateTime;
            TimeSpan clockSkew = (row.ServerUtc - applicationUtc).Duration();
            Dictionary<string, object> data = new()
            {
                ["databaseStatus"] = row.DatabaseStatus,
                ["updateability"] = row.Updateability,
                ["latencyMs"] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                ["clockSkewSeconds"] = Math.Round(clockSkew.TotalSeconds, 2),
            };

            if (!string.Equals(row.DatabaseStatus, "ONLINE", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(row.Updateability, "READ_WRITE", StringComparison.OrdinalIgnoreCase))
            {
                return HealthCheckResult.Unhealthy("پایگاه داده SQL Server آنلاین یا قابل نوشتن نیست.", data: data);
            }

            if (clockSkew > UnhealthyClockSkew)
                return HealthCheckResult.Unhealthy("اختلاف ساعت SQL Server و برنامه بیش از حد مجاز است.", data: data);

            if (stopwatch.Elapsed > DegradedLatency || clockSkew > DegradedClockSkew)
                return HealthCheckResult.Degraded("SQL Server در دسترس است، اما نیاز به بررسی دارد.", data: data);

            return HealthCheckResult.Healthy("SQL Server آنلاین، قابل نوشتن و پاسخ‌گو است.", data);
        }
        catch (Exception)
        {
            stopwatch.Stop();
            return HealthCheckResult.Unhealthy(
                "SQL Server در دسترس نیست.",
                data: new Dictionary<string, object>
                {
                    ["latencyMs"] = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2),
                });
        }
    }

    private sealed record SqlServerReadinessRow(
        string DatabaseStatus,
        string Updateability,
        DateTime ServerUtc);
}
