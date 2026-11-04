namespace SmsHubNext.Features.DatabaseMonitoring;

public sealed record DatabaseCollectionStatus(DateTime? LastAttemptUtc, DateTime? LastSuccessUtc, string? Error);

public sealed class DatabaseMonitoringCollectorState
{
    private DatabaseCollectionStatus _status = new(null, null, null);
    public DatabaseCollectionStatus Status => Volatile.Read(ref _status);
    public void Succeeded(DateTime now) => Volatile.Write(ref _status, new(now, now, null));
    public void Failed(DateTime now) => Volatile.Write(ref _status, new(now, Status.LastSuccessUtc,
        "آخرین جمع‌آوری خودکار کامل نشد؛ گزارش خطای برنامه و دسترسی پایش SQL Server را بررسی کنید."));
}
