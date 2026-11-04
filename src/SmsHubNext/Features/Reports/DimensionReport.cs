using System.Globalization;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Reports;

public enum DimensionReportKind { Geography = 1, MessageType, GeographyMessageType, GeographyMonth, MessageTypeMonth }

public sealed class DimensionReportRequest
{
    public MessageReportRequest Filters { get; init; } = new();
    public DimensionReportKind Kind { get; init; } = DimensionReportKind.Geography;
    public byte GeographyLevel { get; init; } = 1;

    public Result Validate()
    {
        Result filters = Filters.Validate();
        if (filters.IsFailure) return filters;
        if (!Enum.IsDefined(Kind) || GeographyLevel is < 1 or > 3)
            return Error.Validation("reports.dimension_invalid", "نوع گزارش یا سطح جغرافیایی معتبر نیست.");
        if (!TryDate(Filters.FromJalali, out DateTime from) || !TryDate(Filters.ToJalali, out DateTime to))
            return Error.Validation("reports.date_invalid", "تاریخ شمسی معتبر وارد کنید.");
        if (new PersianCalendar().GetYear(to) * 12 + new PersianCalendar().GetMonth(to)
            - new PersianCalendar().GetYear(from) * 12 - new PersianCalendar().GetMonth(from) > 60)
            return Error.Validation("reports.range_too_large", "بازه گزارش حداکثر پنج سال است.");
        return Result.Success();
    }

    private static bool TryDate(string text, out DateTime date)
    {
        date = default;
        if (text.Length != 10 || text.Where((character, index) => index != 4 && index != 7).Any(character => character is < '0' or > '9')) return false;
        try
        {
            date = new PersianCalendar().ToDateTime(int.Parse(text[..4], CultureInfo.InvariantCulture), int.Parse(text[5..7], CultureInfo.InvariantCulture), int.Parse(text[8..], CultureInfo.InvariantCulture), 0, 0, 0, 0);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}

public sealed class DimensionReportRow
{
    public int? GeoSectionId { get; init; }
    public string GeoName { get; init; } = string.Empty;
    public byte? MessageTypeId { get; init; }
    public string MessageTypeName { get; init; } = string.Empty;
    public string JalaliMonth { get; init; } = string.Empty;
    public long MessageCount { get; init; }
    public long SegmentCount { get; init; }
    public decimal TotalCost { get; init; }
    public long AcceptedCount { get; init; }
    public long DeliveredCount { get; init; }
    public long UndeliveredCount { get; init; }
    public long PendingCount { get; init; }
    public long UnknownCount { get; init; }
    public decimal DeliveryRate => AcceptedCount == 0 ? 0 : decimal.Round(DeliveredCount * 100m / AcceptedCount, 4);
}

