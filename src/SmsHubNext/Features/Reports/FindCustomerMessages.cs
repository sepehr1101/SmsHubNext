using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Reports;

/// <summary>Exactly one complete bill reference or recipient, within an inclusive Jalali date range.</summary>
public sealed class FindCustomerMessagesRequest
{
    public string? BillId { get; init; }
    public string? MobileNumber { get; init; }
    public string FromJalali { get; init; } = string.Empty;
    public string ToJalali { get; init; } = string.Empty;
    public string? BeforeDate { get; init; }
    public long? BeforeId { get; init; }

    public Result<SearchMessagesRequest> ToSearch()
    {
        bool bill = !string.IsNullOrWhiteSpace(BillId);
        bool phone = !string.IsNullOrWhiteSpace(MobileNumber);
        if (bill == phone)
            return Error.Validation("reports.search_identifier_required", "دقیقاً یکی از شناسه قبض یا شماره تلفن را وارد کنید.");
        bool dateCursor = !string.IsNullOrWhiteSpace(BeforeDate);
        if (dateCursor != BeforeId.HasValue)
            return Error.Validation("reports.search_cursor_invalid", "تاریخ و شناسهٔ صفحهٔ بعد باید با هم ارسال شوند.");

        SearchMessagesRequest query = new SearchMessagesRequest
        {
            FromJalali = FromJalali, ToJalali = ToJalali,
            Field = bill ? MessageSearchField.BillId : MessageSearchField.MobileNumber,
            Value = bill ? BillId! : MobileNumber!,
            Before = BeforeId.HasValue ? new(BeforeDate!, BeforeId.Value) : null,
        }.Normalize();
        Result validation = query.Validate();
        return validation.IsFailure ? validation.Error! : query;
    }
}
