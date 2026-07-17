using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmsHubNext.Shared.Health;

public sealed class BackgroundWorkersHealthCheck : IHealthCheck
{
    private static readonly TimeSpan StartupGracePeriod = TimeSpan.FromSeconds(30);

    private readonly BackgroundWorkerHealthMonitor _monitor;
    private readonly TimeProvider _clock;
    private readonly IReadOnlyList<BackgroundWorkerExpectation> _expectations;

    public BackgroundWorkersHealthCheck(
        BackgroundWorkerHealthMonitor monitor,
        TimeProvider clock,
        IConfiguration configuration)
    {
        _monitor = monitor;
        _clock = clock;

        List<BackgroundWorkerExpectation> expectations =
        [
            new BackgroundWorkerExpectation(BackgroundWorkerNames.Dispatch, TimeSpan.FromMinutes(2)),
            new BackgroundWorkerExpectation(BackgroundWorkerNames.DeliveryReports, TimeSpan.FromMinutes(5)),
        ];

        if (configuration.GetValue("InboundPolling:Enabled", false))
            expectations.Add(new BackgroundWorkerExpectation(BackgroundWorkerNames.Inbound, TimeSpan.FromMinutes(5)));

        _expectations = expectations;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<BackgroundWorkerHealthSnapshot> snapshots = _monitor.GetSnapshots();
        Dictionary<string, BackgroundWorkerHealthSnapshot> byName = snapshots.ToDictionary(
            snapshot => snapshot.Name,
            StringComparer.Ordinal);
        List<BackgroundWorkerHealthData> workers = [];
        bool degraded = false;

        foreach (BackgroundWorkerExpectation expectation in _expectations)
        {
            if (!byName.TryGetValue(expectation.Name, out BackgroundWorkerHealthSnapshot? snapshot))
            {
                bool starting = now - _monitor.CreatedAtUtc <= StartupGracePeriod;
                degraded |= !starting;
                workers.Add(new BackgroundWorkerHealthData(
                    expectation.Name,
                    starting ? "starting" : "missing",
                    false,
                    null,
                    null,
                    null,
                    0,
                    expectation.StaleAfter.TotalSeconds));
                continue;
            }

            string status = ResolveStatus(snapshot, expectation.StaleAfter, now);
            if (!string.Equals(status, "healthy", StringComparison.Ordinal) &&
                !string.Equals(status, "starting", StringComparison.Ordinal))
            {
                degraded = true;
            }

            workers.Add(new BackgroundWorkerHealthData(
                expectation.Name,
                status,
                snapshot.IsRunning,
                snapshot.StartedAtUtc,
                snapshot.LastSucceededAtUtc,
                snapshot.LastFailedAtUtc,
                snapshot.ConsecutiveFailures,
                expectation.StaleAfter.TotalSeconds));
        }

        Dictionary<string, object> data = new()
        {
            ["workers"] = workers,
        };

        if (degraded)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "One or more background workers need attention.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Background workers are running.", data));
    }

    private static string ResolveStatus(
        BackgroundWorkerHealthSnapshot snapshot,
        TimeSpan staleAfter,
        DateTime now)
    {
        if (!snapshot.IsRunning)
            return "stopped";

        if (snapshot.ConsecutiveFailures > 0)
            return "failing";

        if (snapshot.LastSucceededAtUtc is null)
            return "starting";

        return now - snapshot.LastSucceededAtUtc > staleAfter ? "stale" : "healthy";
    }

    private sealed record BackgroundWorkerExpectation(string Name, TimeSpan StaleAfter);
}

public static class BackgroundWorkerNames
{
    public const string Dispatch = "dispatch";
    public const string DeliveryReports = "delivery-reports";
    public const string Inbound = "inbound";
}

public sealed record BackgroundWorkerHealthData(
    string Name,
    string Status,
    bool IsRunning,
    DateTime? StartedAtUtc,
    DateTime? LastSucceededAtUtc,
    DateTime? LastFailedAtUtc,
    int ConsecutiveFailures,
    double StaleAfterSeconds);
