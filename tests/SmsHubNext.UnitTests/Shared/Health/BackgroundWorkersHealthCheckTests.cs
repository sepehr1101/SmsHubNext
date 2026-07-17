using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SmsHubNext.Shared.Health;
using SmsHubNext.UnitTests.Shared;
using Xunit;

namespace SmsHubNext.UnitTests.Shared.Health;

public sealed class BackgroundWorkersHealthCheckTests
{
    [Fact]
    public async Task Reports_healthy_when_required_workers_are_running_and_current()
    {
        ManualTimeProvider clock = NewClock();
        BackgroundWorkerHealthMonitor monitor = new(clock);
        ReportRequiredWorkersHealthy(monitor);
        BackgroundWorkersHealthCheck check = NewCheck(monitor, clock);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task Reports_degraded_when_dispatch_heartbeat_is_stale()
    {
        ManualTimeProvider clock = NewClock();
        BackgroundWorkerHealthMonitor monitor = new(clock);
        ReportRequiredWorkersHealthy(monitor);
        clock.Advance(TimeSpan.FromMinutes(3));
        monitor.ReportSucceeded(BackgroundWorkerNames.DeliveryReports);
        BackgroundWorkersHealthCheck check = NewCheck(monitor, clock);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
        IReadOnlyList<BackgroundWorkerHealthData> workers =
            Assert.IsAssignableFrom<IReadOnlyList<BackgroundWorkerHealthData>>(result.Data["workers"]);
        BackgroundWorkerHealthData dispatch = Assert.Single(
            workers,
            worker => worker.Name == BackgroundWorkerNames.Dispatch);
        Assert.Equal("stale", dispatch.Status);
    }

    [Fact]
    public async Task Reports_degraded_after_worker_cycle_failure()
    {
        ManualTimeProvider clock = NewClock();
        BackgroundWorkerHealthMonitor monitor = new(clock);
        ReportRequiredWorkersHealthy(monitor);
        monitor.ReportFailed(BackgroundWorkerNames.Dispatch);
        BackgroundWorkersHealthCheck check = NewCheck(monitor, clock);

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    private static BackgroundWorkersHealthCheck NewCheck(
        BackgroundWorkerHealthMonitor monitor,
        TimeProvider clock)
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        return new BackgroundWorkersHealthCheck(monitor, clock, configuration);
    }

    private static void ReportRequiredWorkersHealthy(BackgroundWorkerHealthMonitor monitor)
    {
        monitor.ReportStarted(BackgroundWorkerNames.Dispatch);
        monitor.ReportSucceeded(BackgroundWorkerNames.Dispatch);
        monitor.ReportStarted(BackgroundWorkerNames.DeliveryReports);
        monitor.ReportSucceeded(BackgroundWorkerNames.DeliveryReports);
    }

    private static ManualTimeProvider NewClock() =>
        new(new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero));
}
