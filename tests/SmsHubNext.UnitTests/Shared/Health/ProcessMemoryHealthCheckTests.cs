using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmsHubNext.Shared.Health;
using Xunit;

namespace SmsHubNext.UnitTests.Shared.Health;

public sealed class ProcessMemoryHealthCheckTests
{
    [Fact]
    public async Task Reports_memory_values_needed_by_dashboard()
    {
        ProcessMemoryHealthCheck check = new();

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.True(result.Status is HealthStatus.Healthy or HealthStatus.Degraded);
        Assert.True(Assert.IsType<long>(result.Data["workingSetBytes"]) > 0);
        Assert.True(Assert.IsType<long>(result.Data["managedHeapBytes"]) > 0);
        Assert.True(result.Data.ContainsKey("pressurePercent"));
        Assert.True(result.Data.ContainsKey("totalAvailableMemoryBytes"));
    }
}
