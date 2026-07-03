using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.Batches;

/// <summary>An operator-facing timeline entry for one batch.</summary>
public sealed record BatchEvent(
    long Id,
    long MessageBatchId,
    DateTime EventTimeUtc,
    MessageBatchEventType EventType,
    BatchStatus? BatchStatus,
    BatchStatusReason? BatchStatusReason,
    int? ProviderResultCode,
    string? Detail);
