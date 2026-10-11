namespace SmsHubNext.App.Features.Reports;

public sealed record MessageReportStats(long MessageCount, long SegmentCount, decimal TotalCost, long PendingCount, long DeliveredCount, long UndeliveredCount, long ExpiredCount, long UnknownCount, decimal DeliveryRate);
public sealed record MessageReportSummary(MessageReportStats Totals);
public sealed record ProviderMessageReportRow(byte ProviderId, string ProviderCode, string ProviderName, MessageReportStats Stats);
public sealed record MessageTypeReportRow(byte MessageTypeId, string MessageTypeCode, string MessageTypeName, MessageReportStats Stats);
public sealed record JalaliMonthMessageReportRow(string JalaliMonth, MessageReportStats Stats);
