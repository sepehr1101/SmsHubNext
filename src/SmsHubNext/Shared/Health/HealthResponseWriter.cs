using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace SmsHubNext.Shared.Health;

/// <summary>
/// Stable, secret-free health response consumed by operators and the future React dashboard.
/// </summary>
public sealed class HealthResponseWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TimeProvider _clock;
    private readonly IHostEnvironment _environment;
    private readonly DateTime _startedAtUtc;
    private readonly string _version;

    public HealthResponseWriter(TimeProvider clock, IHostEnvironment environment)
    {
        _clock = clock;
        _environment = environment;
        _startedAtUtc = _clock.GetUtcNow().UtcDateTime;
        _version = ResolveVersion();
    }

    public async Task WriteAsync(HttpContext context, HealthReport report)
    {
        DateTime checkedAtUtc = _clock.GetUtcNow().UtcDateTime;
        List<HealthCheckEntryResponse> checks = report.Entries
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new HealthCheckEntryResponse(
                entry.Key,
                FormatStatus(entry.Value.Status),
                entry.Value.Description,
                Math.Round(entry.Value.Duration.TotalMilliseconds, 2),
                entry.Value.Data))
            .ToList();

        HealthEndpointResponse response = new(
            "SmsHubNext",
            _version,
            _environment.EnvironmentName,
            FormatStatus(report.Status),
            checkedAtUtc,
            Math.Max(0L, Convert.ToInt64(Math.Floor((checkedAtUtc - _startedAtUtc).TotalSeconds))),
            Math.Round(report.TotalDuration.TotalMilliseconds, 2),
            checks);

        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            response,
            JsonOptions,
            context.RequestAborted);
    }

    private static string FormatStatus(HealthStatus status) => status.ToString().ToLowerInvariant();

    private static string ResolveVersion()
    {
        Assembly? assembly = Assembly.GetEntryAssembly();
        AssemblyInformationalVersionAttribute? informationalVersion =
            assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();

        return informationalVersion?.InformationalVersion
            ?? assembly?.GetName().Version?.ToString()
            ?? "unknown";
    }
}

public sealed record HealthEndpointResponse(
    string Service,
    string Version,
    string Environment,
    string Status,
    DateTime CheckedAtUtc,
    long UptimeSeconds,
    double TotalDurationMs,
    IReadOnlyList<HealthCheckEntryResponse> Checks);

public sealed record HealthCheckEntryResponse(
    string Name,
    string Status,
    string? Description,
    double DurationMs,
    IReadOnlyDictionary<string, object> Data);
