namespace SmsHubNext.Features.DatabaseMonitoring;

public enum DatabaseMonitorSection { Overview, Tables, Indexes, Statistics, Requests, Queries, Operations, History, Alerts }

// Shared projection for small diagnostic result sets. Metrics stay numeric for grid sorting and Excel.
public sealed class DatabaseReading
{
    public string Name { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public decimal? Metric1 { get; init; }
    public decimal? Metric2 { get; init; }
    public decimal? Metric3 { get; init; }
    public DateTime? AtUtc { get; init; }
    public DateTime? FirstObservedAtUtc { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? ObjectId { get; init; }
    public int? IndexId { get; init; }
    public int? PartitionNumber { get; init; }
}

public sealed record DatabaseMonitorReport(IReadOnlyList<DatabaseReading> Rows, DateTime CapturedAtUtc, string? Notice = null);
public sealed record DatabaseGrowthPoint(DateTime DayUtc, decimal DataMb, decimal IndexMb);

