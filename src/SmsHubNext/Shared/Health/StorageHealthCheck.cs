using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmsHubNext.Shared.Health;

/// <summary>
/// Checks only storage actually used by this deployable: its content root and the configured Data
/// Protection key-ring location. SQL Server file volumes belong to SQL operations, especially when
/// SQL Server is remote, and are intentionally not inspected here.
/// </summary>
public sealed class StorageHealthCheck : IHealthCheck
{
    private const double DegradedFreePercent = 10d;
    private const double UnhealthyFreePercent = 5d;

    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;

    public StorageHealthCheck(IConfiguration configuration, IHostEnvironment environment)
    {
        _configuration = configuration;
        _environment = environment;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            List<StorageLocation> locations = BuildLocations();
            List<StorageVolumeHealthData> volumes = [];
            bool degraded = false;
            bool unhealthy = false;

            foreach (StorageLocation location in locations)
            {
                string fullPath = Path.GetFullPath(location.Path);
                string? root = Path.GetPathRoot(fullPath);
                bool directoryExists = Directory.Exists(fullPath);

                if (string.IsNullOrWhiteSpace(root))
                {
                    degraded = true;
                    volumes.Add(new StorageVolumeHealthData(
                        location.Purpose,
                        directoryExists,
                        false,
                        0,
                        0,
                        0));
                    continue;
                }

                DriveInfo drive = new(root);
                if (!drive.IsReady)
                {
                    unhealthy = true;
                    volumes.Add(new StorageVolumeHealthData(
                        location.Purpose,
                        directoryExists,
                        false,
                        0,
                        0,
                        0));
                    continue;
                }

                double freePercent = drive.TotalSize > 0
                    ? Math.Round((double)drive.AvailableFreeSpace / drive.TotalSize * 100d, 2)
                    : 0d;

                volumes.Add(new StorageVolumeHealthData(
                    location.Purpose,
                    directoryExists,
                    true,
                    drive.TotalSize,
                    drive.AvailableFreeSpace,
                    freePercent));

                if (!directoryExists || freePercent < DegradedFreePercent)
                    degraded = true;

                if (freePercent < UnhealthyFreePercent)
                    unhealthy = true;
            }

            Dictionary<string, object> data = new()
            {
                ["degradedFreePercent"] = DegradedFreePercent,
                ["unhealthyFreePercent"] = UnhealthyFreePercent,
                ["volumes"] = volumes,
            };

            if (unhealthy)
                return Task.FromResult(HealthCheckResult.Unhealthy("Required storage is critically low or unavailable.", data: data));

            if (degraded)
                return Task.FromResult(HealthCheckResult.Degraded("Required storage needs attention.", data: data));

            return Task.FromResult(HealthCheckResult.Healthy("Required storage is available.", data));
        }
        catch (Exception)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Required storage could not be inspected."));
        }
    }

    private List<StorageLocation> BuildLocations()
    {
        List<StorageLocation> locations =
        [
            new StorageLocation("application", _environment.ContentRootPath),
        ];

        string? keyRingPath = _configuration["DataProtection:KeyRingPath"];
        if (!string.IsNullOrWhiteSpace(keyRingPath))
        {
            string resolvedKeyRingPath = Path.IsPathRooted(keyRingPath)
                ? keyRingPath
                : Path.Combine(_environment.ContentRootPath, keyRingPath);
            locations.Add(new StorageLocation("dataProtectionKeyRing", resolvedKeyRingPath));
        }

        return locations;
    }

    private sealed record StorageLocation(string Purpose, string Path);
}

public sealed record StorageVolumeHealthData(
    string Purpose,
    bool DirectoryExists,
    bool DriveReady,
    long TotalBytes,
    long AvailableBytes,
    double FreePercent);
