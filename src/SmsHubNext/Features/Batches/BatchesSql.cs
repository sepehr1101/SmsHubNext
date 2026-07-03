namespace SmsHubNext.Features.Batches;

internal static class BatchesSql
{
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
