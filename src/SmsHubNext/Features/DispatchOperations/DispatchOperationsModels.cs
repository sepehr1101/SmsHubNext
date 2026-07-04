using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.DispatchOperations;

public sealed record DispatchOperationsSummary(
    DispatchQueueTotals Totals,
    IReadOnlyList<DispatchBatchStatusCount> BatchStatuses,
    IReadOnlyList<DispatchMessageStatusCount> MessageStatuses,
    IReadOnlyList<DispatchFailureReasonCount> FailureReasons);

public sealed record DispatchQueueTotals(
    long BatchCount,
    long MessageCount,
    decimal TotalCost,
    long DueBatchCount,
    long ScheduledRetryBatchCount,
    long AwaitingConfirmationBatchCount,
    long HeldBatchCount,
    long DispatchFailedBatchCount,
    int MaxDispatchAttemptCount,
    DateTime? OldestOpenBatchReceivedAtUtc,
    DateTime? OldestDueBatchReceivedAtUtc);

public sealed record DispatchBatchStatusCount(BatchStatus Status, long BatchCount, long MessageCount);

public sealed record DispatchMessageStatusCount(SendStatus Status, long MessageCount);

public sealed record DispatchFailureReasonCount(BatchStatusReason Reason, long BatchCount);

public sealed record DispatchOperationsBatchRow(
    long Id,
    string SubmitDateJalali,
    DateTime ReceivedAtUtc,
    short CustomerId,
    byte ProviderId,
    string ProviderCode,
    string ProviderName,
    int MessageCount,
    int SegmentCount,
    decimal TotalCost,
    BatchStatus Status,
    BatchStatusReason? StatusReason,
    int DispatchAttemptCount,
    DateTime? NextDispatchAtUtc,
    DateTime StatusChangedAtUtc,
    long QueuedMessageCount,
    long AwaitingConfirmationMessageCount,
    long RejectedMessageCount,
    MessageBatchEventType? LastEventType,
    DateTime? LastEventTimeUtc,
    string? LastEventDetail);

public sealed record DispatchOperationsBatchPage(
    int Page,
    int Take,
    long TotalCount,
    IReadOnlyList<DispatchOperationsBatchRow> Items);
