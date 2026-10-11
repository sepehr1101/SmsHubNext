using SmsHubNext.App.Features.DeliveryReports;

namespace SmsHubNext.App.Features.DispatchOperations;
public enum BatchStatus : byte { Received=1, Dispatching=2, DispatchCompleted=3, DispatchPartiallyFailed=4, Held=5, Rejected=6, DispatchFailed=7 }
public enum SendStatus : byte { Queued=1, Submitted=2, Sent=3, Rejected=4, Unknown=5, AwaitingConfirmation=6 }
public enum BatchStatusReason : byte { InsufficientProviderCredit=1, InsufficientCustomerBalance=2, DispatchRetriesExhausted=3, ManualReviewRequired=4, InvalidProviderCredentials=5 }
public enum MessageBatchEventType : byte { Accepted=1, DispatchStarted=2, DispatchResumed=3, Requeued=4, AwaitingConfirmation=5, Held=6, MessageRejected=7, DispatchCompleted=8, DispatchPartiallyFailed=9, DispatchFailed=10, DeliveryUpdated=11, DispatchRetryRequested=12 }
public sealed record DispatchQueueTotals(long BatchCount,long MessageCount,decimal TotalCost,long DueBatchCount,long ScheduledRetryBatchCount,long AwaitingConfirmationBatchCount,long HeldBatchCount,long DispatchFailedBatchCount,int MaxDispatchAttemptCount,DateTime? OldestOpenBatchReceivedAtUtc,DateTime? OldestDueBatchReceivedAtUtc);
public sealed record DispatchBatchStatusCount(BatchStatus Status,long BatchCount,long MessageCount);
public sealed record DispatchMessageStatusCount(SendStatus Status,long MessageCount);
public sealed record DispatchFailureReasonCount(BatchStatusReason Reason,long BatchCount);
public sealed record DispatchOperationsSummary(DispatchQueueTotals Totals,IReadOnlyList<DispatchBatchStatusCount> BatchStatuses,IReadOnlyList<DispatchMessageStatusCount> MessageStatuses,IReadOnlyList<DispatchFailureReasonCount> FailureReasons);
public sealed record DispatchOperationsBatchRow(long Id,string SubmitDateJalali,DateTime ReceivedAtUtc,short CustomerId,byte ProviderId,string ProviderCode,string ProviderName,int MessageCount,int SegmentCount,decimal TotalCost,BatchStatus Status,BatchStatusReason? StatusReason,int DispatchAttemptCount,DateTime? NextDispatchAtUtc,DateTime StatusChangedAtUtc,long QueuedMessageCount,long AwaitingConfirmationMessageCount,long RejectedMessageCount,MessageBatchEventType? LastEventType,DateTime? LastEventTimeUtc,string? LastEventDetail);
public sealed record DispatchOperationsBatchPage(int Page,int Take,long TotalCount,IReadOnlyList<DispatchOperationsBatchRow> Items);
public sealed record BatchDetails(long Id,string SubmitDateJalali,DateTime ReceivedAtUtc,short CustomerId,int ApiKeyId,short SenderLineId,byte ProviderId,string? ClientBatchId,int MessageCount,int SegmentCount,decimal TotalCost,BatchStatus Status,BatchStatusReason? StatusReason,int? ProviderResultCode,DateTime? DispatchStartedAtUtc,DateTime? FinishedAtUtc,DateTime StatusChangedAtUtc,int DispatchAttemptCount,DateTime? NextDispatchAtUtc);
public sealed record BatchMessageDetails(long Id,string MobileNumber,SendStatus Status,CurrentDeliveryStatus DeliveryStatus,byte SegmentCount,decimal TotalCost,string? ClientCorrelatedId,DateTime SubmittedAtUtc);
public sealed record BatchEventDetails(long Id,long MessageBatchId,DateTime EventTimeUtc,MessageBatchEventType EventType,BatchStatus? BatchStatus,BatchStatusReason? BatchStatusReason,int? ProviderResultCode,string? Detail);
public sealed record RetryDispatchResponse(long BatchId);
