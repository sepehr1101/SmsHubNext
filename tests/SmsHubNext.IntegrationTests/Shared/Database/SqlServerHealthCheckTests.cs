using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmsHubNext.Shared.Database;
using Testcontainers.MsSql;
using Xunit;

namespace SmsHubNext.IntegrationTests.Shared.Database;

/// <summary>
/// Verifies the health check against a real SQL Server (Testcontainers — requires Docker).
/// </summary>
public sealed class SqlServerHealthCheckTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlServer = new MsSqlBuilder(Literals.sqlImage).Build();

    public Task InitializeAsync() => _sqlServer.StartAsync();

    public Task DisposeAsync() => _sqlServer.DisposeAsync().AsTask();

    [Fact]
    public async Task Reports_healthy_when_sql_server_is_reachable()
    {
        SqlServerHealthCheck check = new SqlServerHealthCheck(
            new Db(_sqlServer.GetConnectionString()),
            TimeProvider.System);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("ONLINE", result.Data["databaseStatus"]);
        Assert.Equal("READ_WRITE", result.Data["updateability"]);
    }
}

/// <summary>
/// The unreachable-database path; needs no container (it connects to a dead port).
/// </summary>
public sealed class SqlServerHealthCheckUnreachableTests
{
    [Fact]
    public async Task Reports_unhealthy_when_sql_server_is_unreachable()
    {
        Db db = new Db(
            "Server=localhost,1;Database=none;User Id=sa;Password=x;Connect Timeout=1;TrustServerCertificate=True");
        SqlServerHealthCheck check = new SqlServerHealthCheck(db, TimeProvider.System);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
