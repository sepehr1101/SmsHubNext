namespace SmsHubNext.Features.DispatchOperations;

internal static class DispatchOperationsSql
{
    private const string Filters =
        """
        WHERE (@FromJalali IS NULL OR b.SubmitDateJalali >= @FromJalali)
          AND (@ToJalali IS NULL OR b.SubmitDateJalali <= @ToJalali)
          AND (@CustomerId IS NULL OR b.CustomerId = @CustomerId)
          AND (@ProviderId IS NULL OR b.ProviderId = @ProviderId)
          AND (@Status IS NULL OR b.Status = @Status)
          AND (
              @OnlyProblems = 0
              OR b.Status IN (4, 5, 7) -- DispatchPartiallyFailed, Held, DispatchFailed
              OR EXISTS (
                  SELECT 1
                  FROM dbo.Message problemMessage
                  WHERE problemMessage.MessageBatchId = b.Id AND problemMessage.Status = 6
              )
          )
        """;

    public const string Totals =
        $"""
        SELECT
            COUNT_BIG(*) AS BatchCount,
            COALESCE(SUM(CAST(b.MessageCount AS BIGINT)), 0) AS MessageCount,
            COALESCE(SUM(b.TotalCost), 0) AS TotalCost,
            SUM(CASE WHEN b.Status = 1 AND (b.NextDispatchAtUtc IS NULL OR b.NextDispatchAtUtc <= @Now) THEN 1 ELSE 0 END) AS DueBatchCount,
            SUM(CASE WHEN b.Status = 1 AND b.NextDispatchAtUtc > @Now THEN 1 ELSE 0 END) AS ScheduledRetryBatchCount,
            SUM(CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Message awaiting WHERE awaiting.MessageBatchId = b.Id AND awaiting.Status = 6
            ) THEN 1 ELSE 0 END) AS AwaitingConfirmationBatchCount,
            SUM(CASE WHEN b.Status = 5 THEN 1 ELSE 0 END) AS HeldBatchCount,
            SUM(CASE WHEN b.Status = 7 THEN 1 ELSE 0 END) AS DispatchFailedBatchCount,
            COALESCE(MAX(b.DispatchAttemptCount), 0) AS MaxDispatchAttemptCount,
            MIN(CASE WHEN b.Status IN (1, 2, 5) THEN b.ReceivedAtUtc END) AS OldestOpenBatchReceivedAtUtc,
            MIN(CASE WHEN b.Status = 1 AND (b.NextDispatchAtUtc IS NULL OR b.NextDispatchAtUtc <= @Now) THEN b.ReceivedAtUtc END) AS OldestDueBatchReceivedAtUtc
        FROM dbo.MessageBatch b
        {Filters};
        """;

    public const string BatchStatuses =
        $"""
        SELECT b.Status, COUNT_BIG(*) AS BatchCount, COALESCE(SUM(CAST(b.MessageCount AS BIGINT)), 0) AS MessageCount
        FROM dbo.MessageBatch b
        {Filters}
        GROUP BY b.Status
        ORDER BY b.Status;
        """;

    public const string MessageStatuses =
        $"""
        SELECT m.Status, COUNT_BIG(*) AS MessageCount
        FROM dbo.MessageBatch b
        INNER JOIN dbo.Message m ON m.MessageBatchId = b.Id
        {Filters}
        GROUP BY m.Status
        ORDER BY m.Status;
        """;

    public const string FailureReasons =
        $"""
        SELECT b.StatusReason AS Reason, COUNT_BIG(*) AS BatchCount
        FROM dbo.MessageBatch b
        {Filters}
          AND b.StatusReason IS NOT NULL
        GROUP BY b.StatusReason
        ORDER BY BatchCount DESC, b.StatusReason;
        """;

    public const string CountBatches =
        $"""
        SELECT COUNT_BIG(*)
        FROM dbo.MessageBatch b
        {Filters};
        """;

    public const string ListBatches =
        $"""
        SELECT
            b.Id,
            b.SubmitDateJalali,
            b.ReceivedAtUtc,
            b.CustomerId,
            b.ProviderId,
            p.Code AS ProviderCode,
            p.Name AS ProviderName,
            b.MessageCount,
            b.SegmentCount,
            b.TotalCost,
            b.Status,
            b.StatusReason,
            b.DispatchAttemptCount,
            b.NextDispatchAtUtc,
            b.StatusChangedAtUtc,
            messageCounts.QueuedMessageCount,
            messageCounts.AwaitingConfirmationMessageCount,
            messageCounts.RejectedMessageCount,
            latest.EventType AS LastEventType,
            latest.EventTimeUtc AS LastEventTimeUtc,
            latest.Detail AS LastEventDetail
        FROM dbo.MessageBatch b
        INNER JOIN dbo.Provider p ON p.Id = b.ProviderId
        OUTER APPLY (
            SELECT
                COALESCE(SUM(CASE WHEN m.Status = 1 THEN 1 ELSE 0 END), 0) AS QueuedMessageCount,
                COALESCE(SUM(CASE WHEN m.Status = 6 THEN 1 ELSE 0 END), 0) AS AwaitingConfirmationMessageCount,
                COALESCE(SUM(CASE WHEN m.Status = 4 THEN 1 ELSE 0 END), 0) AS RejectedMessageCount
            FROM dbo.Message m
            WHERE m.MessageBatchId = b.Id
        ) messageCounts
        OUTER APPLY (
            SELECT TOP (1) e.EventType, e.EventTimeUtc, e.Detail
            FROM dbo.MessageBatchEvent e
            WHERE e.MessageBatchId = b.Id
            ORDER BY e.EventTimeUtc DESC, e.Id DESC
        ) latest
        {Filters}
        ORDER BY
            CASE WHEN b.Status IN (5, 7) THEN 0 ELSE 1 END,
            b.StatusChangedAtUtc DESC,
            b.Id DESC
        OFFSET @Offset ROWS FETCH NEXT @Take ROWS ONLY;
        """;
}
