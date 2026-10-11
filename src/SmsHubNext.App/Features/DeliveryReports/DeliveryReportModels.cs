namespace SmsHubNext.App.Features.DeliveryReports;

public enum DeliveryReportStatus : byte { Delivered = 1, Undelivered = 2, Expired = 3, Rejected = 4, Unknown = 5 }
public enum MessageSendStatus : byte { Queued = 1, Submitted = 2, Sent = 3, Rejected = 4, Unknown = 5, AwaitingConfirmation = 6 }
public enum CurrentDeliveryStatus : byte { Pending = 1, Delivered = 2, Undelivered = 3, Expired = 4, Unknown = 5 }
public sealed record DeliveryReportItem(long Id, string SubmitDateJalali, long MessageId, DeliveryReportStatus NormalizedStatus, int RawStatusCode, DateTime ReceivedAtUtc);
public sealed record BatchMessageItem(long Id, string MobileNumber, MessageSendStatus Status, CurrentDeliveryStatus DeliveryStatus, byte SegmentCount, decimal TotalCost, string? ClientCorrelatedId, DateTime SubmittedAtUtc);
public sealed record IngestDeliveryReportRequest(long MessageId, DeliveryReportStatus Status, int RawStatusCode);
public sealed record IngestDeliveryReportResponse(long ReportId, CurrentDeliveryStatus DeliveryStatus, bool AppliedToReadModel);
