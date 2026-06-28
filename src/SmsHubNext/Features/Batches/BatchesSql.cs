namespace SmsHubNext.Features.Batches;

internal static class BatchesSql
{
    public const string GetById =
        """
        SELECT Id, SubmitDateJalali, ReceivedAtUtc, CustomerId, ApiKeyId, SenderLineId, ProviderId,
               ClientBatchId, MessageCount, SegmentCount, TotalCost, Status, StatusReason,
               ProviderResultCode, DispatchStartedAtUtc, FinishedAtUtc, StatusChangedAtUtc
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
}
