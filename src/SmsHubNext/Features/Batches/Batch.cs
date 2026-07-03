using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.Batches;

/// <summary>
/// The request/accounting header for one send API call — the authoritative current
/// state callers poll after a send (README §4.13).
/// </summary>
public sealed record Batch(
    long Id,
    string SubmitDateJalali,
    DateTime ReceivedAtUtc,
    short CustomerId,
    int ApiKeyId,
    short SenderLineId,
    byte ProviderId,
    string? ClientBatchId,
    int MessageCount,
    int SegmentCount,
    decimal TotalCost,
    BatchStatus Status,
    BatchStatusReason? StatusReason,
    int? ProviderResultCode,
    DateTime? DispatchStartedAtUtc,
    DateTime? FinishedAtUtc,
    DateTime StatusChangedAtUtc,
    int DispatchAttemptCount,
    DateTime? NextDispatchAtUtc);
