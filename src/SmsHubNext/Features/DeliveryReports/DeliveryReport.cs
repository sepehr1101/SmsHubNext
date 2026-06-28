using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.DeliveryReports;

/// <summary>One entry in a message's append-only status-event history (README §4.12).</summary>
public sealed record DeliveryReport(
    long Id,
    string SubmitDateJalali,
    long MessageId,
    DeliveryReportStatus NormalizedStatus,
    int RawStatusCode,
    DateTime ReceivedAtUtc);
