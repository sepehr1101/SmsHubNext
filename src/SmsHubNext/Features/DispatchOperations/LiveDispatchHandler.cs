using Dapper;
using Microsoft.Data.SqlClient;
using SmsHubNext.Shared.Time;
using SmsHubNext.Shared.Database;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.DispatchOperations;

public sealed record LiveBucket(DateTime StartUtc, long Count);
public sealed record LiveTotals(long Queued, long Unconfirmed, long Accepted, long Sent, long Rejected, long Delivered, long DeliveryFailed);
public sealed record LiveSnapshot(DateTime ObservedAtUtc, DateTime FromUtc, LiveTotals Totals, IReadOnlyList<LiveBucket> Buckets)
{
    public DateTime TotalsAtUtc { get; init; }
}
public sealed record LiveMessage(long Id, long MessageBatchId, string MobileNumber, string SenderLine, string ProviderName,
    SendStatus Status, DeliveryStatus DeliveryStatus, DateTime SubmittedAtUtc, DateTime? AcceptedAtUtc,
    int DispatchAttemptCount, string? Body, DateTime? ErrorAtUtc, int? ErrorCode, MessageBatchEventType? ErrorEvent);
public sealed record LiveMessagePage(IReadOnlyList<LiveMessage> Items, bool HasMore);

public static class LiveDispatchBuckets
{
    public static DateTime Floor(DateTime value) => new(value.Ticks - value.Ticks % TimeSpan.FromSeconds(10).Ticks, DateTimeKind.Utc);
    public static IReadOnlyList<LiveBucket> Fill(DateTime end, IEnumerable<LiveBucket> rows)
    {
        Dictionary<DateTime, long> counts = rows.ToDictionary(row => row.StartUtc, row => row.Count);
        return Enumerable.Range(0, 60).Select(index => Floor(end).AddSeconds((index - 60) * 10))
            .Select(start => new LiveBucket(start, counts.GetValueOrDefault(start))).ToList();
    }
}

// Shared per host; concurrent viewers reuse one bounded summary query every ten seconds.
public sealed class LiveDispatchCache
{
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public LiveSnapshot? Snapshot { get; set; }
}

public sealed class LiveDispatchHandler(Db db, TimeProvider clock, LiveDispatchCache cache)
{
    public async Task<Result<LiveSnapshot>> Summary(CancellationToken token)
    {
        await cache.Gate.WaitAsync(token);
        try
        {
            DateTime now = clock.GetUtcNow().UtcDateTime;
            if (cache.Snapshot is { } cached && now - cached.ObservedAtUtc < TimeSpan.FromSeconds(10)) return cached;
            DateTime from = now.AddHours(-24);
            DateTime end = LiveDispatchBuckets.Floor(now);
            await using SqlConnection connection = await db.OpenConnectionAsync(token);
            bool reuseTotals = cache.Snapshot is { } prior && now - prior.TotalsAtUtc < TimeSpan.FromMinutes(1);
            LiveTotals totals = reuseTotals ? cache.Snapshot!.Totals : await connection.QuerySingleAsync<LiveTotals>(new CommandDefinition(
                LiveDispatchSql.Summary, new { From = from, Until = now, FromJalali = DateKey(from), ToJalali = DateKey(now) }, commandTimeout: 10, cancellationToken: token));
            List<LiveBucket> buckets = (await connection.QueryAsync<LiveBucket>(new CommandDefinition(LiveDispatchSql.Chart,
                new { Start = end.AddMinutes(-10), End = end }, commandTimeout: 10, cancellationToken: token))).AsList();
            LiveSnapshot snapshot = new(now, reuseTotals ? cache.Snapshot!.FromUtc : from, totals, LiveDispatchBuckets.Fill(end, buckets))
            { TotalsAtUtc = reuseTotals ? cache.Snapshot!.TotalsAtUtc : now };
            cache.Snapshot = snapshot;
            return snapshot;
        }
        finally { cache.Gate.Release(); }
    }

    public async Task<Result<LiveMessagePage>> Messages(int hours, byte? status, byte? provider, long? batch, string search, int page, CancellationToken token)
    {
        if (hours is < 1 or > 24 || page is < 1 or > 1000 || (status.HasValue && !Enum.IsDefined((SendStatus)status.Value)))
            return Error.Validation("monitor.invalid_filter", "بازه یا وضعیت معتبر نیست.");
        DateTime until = clock.GetUtcNow().UtcDateTime;
        DateTime from = until.AddHours(-hours);
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        List<LiveMessage> items = (await connection.QueryAsync<LiveMessage>(new CommandDefinition(LiveDispatchSql.Messages,
            new { From = from, Until = until, FromJalali = DateKey(from), ToJalali = DateKey(until), Status = status,
                Provider = provider, Batch = batch, Search = search, Offset = (page - 1) * 25 }, commandTimeout: 10, cancellationToken: token))).AsList();
        return new LiveMessagePage(items.Take(25).ToList(), items.Count > 25);
    }

    private static DbString DateKey(DateTime utc) => new() { Value = JalaliDate.FromUtc(utc), IsAnsi = true, IsFixedLength = true, Length = 10 };
}
