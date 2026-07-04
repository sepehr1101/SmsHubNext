using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Reports;

/// <summary>Common filters for statistical message reports.</summary>
public sealed class MessageReportRequest
{
    public string FromJalali { get; init; } = string.Empty;
    public string ToJalali { get; init; } = string.Empty;
    public short? CustomerId { get; init; }
    public byte? ProviderId { get; init; }
    public byte? MessageTypeId { get; init; }
    public int? GeoSectionId { get; init; }

    public MessageReportRequest ForCustomer(short customerId) => new()
    {
        FromJalali = FromJalali,
        ToJalali = ToJalali,
        CustomerId = customerId,
        ProviderId = ProviderId,
        MessageTypeId = MessageTypeId,
        GeoSectionId = GeoSectionId,
    };

    public Result Validate()
    {
        if (!IsValidJalaliDate(FromJalali))
            return Error.Validation("reports.from_jalali_invalid", UserMessages.Reports.FromJalaliInvalid);

        if (!IsValidJalaliDate(ToJalali))
            return Error.Validation("reports.to_jalali_invalid", UserMessages.Reports.ToJalaliInvalid);

        if (string.CompareOrdinal(FromJalali, ToJalali) > 0)
            return Error.Validation("reports.invalid_range", UserMessages.Reports.InvalidRange);

        if (CustomerId <= 0)
            return Error.Validation("reports.customer_invalid", UserMessages.Reports.CustomerInvalid);

        if (ProviderId == 0)
            return Error.Validation("reports.provider_invalid", UserMessages.Reports.ProviderInvalid);

        if (MessageTypeId == 0)
            return Error.Validation("reports.message_type_invalid", UserMessages.Reports.MessageTypeInvalid);

        if (GeoSectionId <= 0)
            return Error.Validation("reports.geo_section_invalid", UserMessages.Reports.GeoSectionInvalid);

        return Result.Success();
    }

    private static bool IsValidJalaliDate(string value)
    {
        if (value.Length != 10)
            return false;

        return char.IsDigit(value[0])
            && char.IsDigit(value[1])
            && char.IsDigit(value[2])
            && char.IsDigit(value[3])
            && value[4] == '/'
            && char.IsDigit(value[5])
            && char.IsDigit(value[6])
            && value[7] == '/'
            && char.IsDigit(value[8])
            && char.IsDigit(value[9]);
    }
}
