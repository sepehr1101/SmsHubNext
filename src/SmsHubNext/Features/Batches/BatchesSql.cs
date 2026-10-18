namespace SmsHubNext.Features.Batches;

internal static class BatchesSql
{
    public const string Browse =
        """
        WITH PageRows AS
        (
            SELECT TOP (@Take) b.Id, b.SubmitDateJalali, b.ReceivedAtUtc, b.CustomerId, b.ProviderId,
                   b.MessageCount, b.SegmentCount, b.TotalCost, b.Status
            FROM dbo.MessageBatch b
            WHERE b.SubmitDateJalali >= @FromJalali AND b.SubmitDateJalali <= @ToJalali
              AND b.ReceivedAtUtc >= @FromUtc AND b.ReceivedAtUtc < @ToUtcExclusive
              AND (@BeforeId IS NULL OR b.SubmitDateJalali < @BeforeDate
                   OR (b.SubmitDateJalali = @BeforeDate AND b.Id < @BeforeId))
            ORDER BY b.SubmitDateJalali DESC, b.Id DESC
        )
        SELECT b.Id, b.SubmitDateJalali, b.ReceivedAtUtc, c.Name AS CustomerName, p.Name AS ProviderName,
               b.MessageCount, b.SegmentCount, b.TotalCost, b.Status
        FROM PageRows b
        INNER JOIN dbo.Customer c ON c.Id = b.CustomerId
        INNER JOIN dbo.Provider p ON p.Id = b.ProviderId
        ORDER BY b.SubmitDateJalali DESC, b.Id DESC
        OPTION (RECOMPILE);
        """;

    public const string ListMessagesPage =
        """
        SELECT TOP (@Take) Id, MobileNumber, Status, DeliveryStatus, SegmentCount, TotalCost, ClientCorrelatedId, SubmittedAtUtc
        FROM dbo.Message
        WHERE SubmitDateJalali = @SubmitDateJalali
          AND MessageBatchId = @BatchId AND (@AfterId IS NULL OR Id > @AfterId)
        ORDER BY Id
        OPTION (RECOMPILE);
        """;

    public const string GetPartition = "SELECT SubmitDateJalali FROM dbo.MessageBatch WHERE Id = @BatchId;";

    public const string Exists =
        "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.MessageBatch WHERE Id = @Id) THEN 1 ELSE 0 END AS bit);";

    public const string GetById =
        """
        SELECT Id, SubmitDateJalali, ReceivedAtUtc, CustomerId, ApiKeyId, SenderLineId, ProviderId,
               ClientBatchId, MessageCount, SegmentCount, TotalCost, Status, StatusReason,
               ProviderResultCode, DispatchStartedAtUtc, FinishedAtUtc, StatusChangedAtUtc,
               DispatchAttemptCount, NextDispatchAtUtc
        FROM dbo.MessageBatch
        WHERE Id = @Id;
        """;

    public const string ListMessages =
        """
        SELECT Id, MobileNumber, Status, DeliveryStatus, SegmentCount, TotalCost, ClientCorrelatedId, SubmittedAtUtc
        FROM dbo.Message
        WHERE MessageBatchId = @BatchId
        ORDER BY Id;
        """;

    public const string ListEvents =
        """
        SELECT Id, MessageBatchId, EventTimeUtc, EventType, BatchStatus, BatchStatusReason,
               ProviderResultCode, Detail
        FROM dbo.MessageBatchEvent
        WHERE MessageBatchId = @BatchId
        ORDER BY EventTimeUtc, Id;
        """;
}
