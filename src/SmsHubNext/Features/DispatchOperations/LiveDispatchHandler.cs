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
public sealed record LiveMessage(long Id, string SubmitDateJalali, long MessageBatchId, string MobileNumber, string SenderLine, string ProviderName,
    SendStatus Status, DeliveryStatus DeliveryStatus, DateTime SubmittedAtUtc, DateTime? AcceptedAtUtc,
    int DispatchAttemptCount, string? Body, DateTime? ErrorAtUtc, int? ErrorCode, MessageBatchEventType? ErrorEvent);
public sealed record LiveMessagePage(IReadOnlyList<LiveMessage> Items, bool HasMore, DateTime UntilUtc, LiveMessageCursor? NextCursor);

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

    public async Task<Result<LiveMessagePage>> Messages(LiveMessageRequest request, CancellationToken token)
    {
        if (request.Hours is < 1 or > 24 || request.PageSize is not (25 or 50 or 100) ||
            request.Batch is <= 0 || request.Provider is 0 ||
            (request.Status.HasValue && !Enum.IsDefined((SendStatus)request.Status.Value)) ||
            (request.Before is { IsValid: false }))
            return Error.Validation("monitor.invalid_filter", "بازه یا وضعیت معتبر نیست.");
        LiveMessageSearch search = LiveMessageSearch.Parse(request.Search);
        if (!search.IsValid)
            return Error.Validation("monitor.invalid_search", "شماره گیرنده یا شناسه پیام معتبر نیست.");
        DateTime until = request.UntilUtc ?? clock.GetUtcNow().UtcDateTime;
        DateTime from = until.AddHours(-request.Hours);
        await using SqlConnection connection = await db.OpenConnectionAsync(token);
        List<LiveMessage> items = (await connection.QueryAsync<LiveMessage>(new CommandDefinition(LiveDispatchSql.Messages,
            new { From = from, Until = until, FromJalali = DateKey(from), ToJalali = DateKey(until), request.Status,
                request.Provider, request.Batch, search.HasSearch, SearchId = search.MessageId,
                SearchMobile = new DbString { Value = search.MobileNumber, IsAnsi = true, Length = 12 },
                BeforeDate = new DbString { Value = request.Before?.SubmitDateJalali, IsAnsi = true, IsFixedLength = true, Length = 10 },
                BeforeId = request.Before?.Id, Take = request.PageSize + 1 }, commandTimeout: 10, cancellationToken: token))).AsList();
        bool hasMore = items.Count > request.PageSize;
        List<LiveMessage> page = items.Take(request.PageSize).ToList();
        LiveMessageCursor? next = hasMore ? new(page[^1].SubmitDateJalali, page[^1].Id) : null;
        return new LiveMessagePage(page, hasMore, until, next);
    }

    private static DbString DateKey(DateTime utc) => new() { Value = JalaliDate.FromUtc(utc), IsAnsi = true, IsFixedLength = true, Length = 10 };
}
