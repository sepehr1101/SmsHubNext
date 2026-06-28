using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.DeliveryReports;

/// <summary>
/// Acknowledgement of an ingested report: the new <c>DeliveryReport</c> id and the
/// resulting denormalized <see cref="DeliveryStatus"/> read model on the message.
/// </summary>
public sealed record IngestDeliveryReportResponse(long ReportId, DeliveryStatus DeliveryStatus);
