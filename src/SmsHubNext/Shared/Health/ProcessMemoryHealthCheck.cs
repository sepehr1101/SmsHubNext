using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmsHubNext.Shared.Health;

/// <summary>
/// Reports process and GC memory for diagnostics. High pressure degrades the service but does not
/// make readiness fail, because a short memory spike must not cause an IIS restart loop.
/// </summary>
public sealed class ProcessMemoryHealthCheck : IHealthCheck
{
    private const double DegradedPressurePercent = 90d;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        double? pressurePercent = memory.HighMemoryLoadThresholdBytes > 0
            ? Math.Round(
                (double)memory.MemoryLoadBytes / memory.HighMemoryLoadThresholdBytes * 100d,
                2)
            : null;

        Dictionary<string, object> data = new()
        {
            ["workingSetBytes"] = Environment.WorkingSet,
            ["managedHeapBytes"] = GC.GetTotalMemory(forceFullCollection: false),
            ["gcHeapSizeBytes"] = memory.HeapSizeBytes,
            ["gcFragmentedBytes"] = memory.FragmentedBytes,
            ["memoryLoadBytes"] = memory.MemoryLoadBytes,
            ["highMemoryLoadThresholdBytes"] = memory.HighMemoryLoadThresholdBytes,
            ["totalAvailableMemoryBytes"] = memory.TotalAvailableMemoryBytes,
            ["pressurePercent"] = pressurePercent ?? 0d,
            ["pressureAvailable"] = pressurePercent.HasValue,
        };

        if (pressurePercent >= DegradedPressurePercent)
        {
            return Task.FromResult(HealthCheckResult.Degraded(
                "Process memory pressure is high.",
                data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy(
            "Process memory pressure is within the expected range.",
            data));
    }
}
