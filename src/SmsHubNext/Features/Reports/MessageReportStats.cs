namespace SmsHubNext.Features.Reports;

/// <summary>Statistical totals over messages; no per-message details are returned.</summary>
public sealed record MessageReportStats(
    long MessageCount,
    long SegmentCount,
    decimal TotalCost,
    long PendingCount,
    long DeliveredCount,
    long UndeliveredCount,
    long ExpiredCount,
    long UnknownCount,
    decimal DeliveryRate);

public sealed record MessageReportSummary(MessageReportStats Totals);

public sealed record ProviderMessageReportRow(
    byte ProviderId,
    string ProviderCode,
    string ProviderName,
    MessageReportStats Stats);

public sealed record MessageTypeReportRow(
    byte MessageTypeId,
    string MessageTypeCode,
    string MessageTypeName,
    MessageReportStats Stats);

public sealed record GeoMessageReportRow(
    int? GeoSectionId,
    byte? SectionType,
    string? GeoSectionCode,
    string GeoSectionName,
    MessageReportStats Stats);

public sealed record JalaliMonthMessageReportRow(string JalaliMonth, MessageReportStats Stats);

public sealed record ProviderMessageTypeGeoReportRow(
    byte ProviderId,
    string ProviderCode,
    string ProviderName,
    byte MessageTypeId,
    string MessageTypeCode,
    string MessageTypeName,
    int? GeoSectionId,
    byte? SectionType,
    string? GeoSectionCode,
    string GeoSectionName,
    MessageReportStats Stats);
