using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.Batches;

/// <summary>
/// A single message within a batch, projected for status polling: its send-lifecycle
/// <see cref="Status"/> and the denormalized current <see cref="DeliveryStatus"/> read
/// model (README §4.10). The body lives in <c>MessageBody</c> and is not returned here.
/// </summary>
public sealed record BatchMessage(
    long Id,
    string MobileNumber,
    SendStatus Status,
    DeliveryStatus DeliveryStatus,
    byte SegmentCount,
    decimal TotalCost,
    string? ClientCorrelatedId,
    DateTime SubmittedAtUtc);
