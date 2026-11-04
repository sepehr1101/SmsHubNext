using System.Globalization;
using DNTPersianUtils.Core;
using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Sms;
using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Reports;

public enum MessageSearchField { BillId, PayId, MobileNumber }

public sealed record MessageSearchCursor(string SubmitDateJalali, long Id);

public sealed record SearchMessagesRequest
{
    internal short? CustomerId { get; init; }
    internal int Limit { get; init; } = SearchMessagesHandler.PageSize;
    public string FromJalali { get; init; } = string.Empty;
    public string ToJalali { get; init; } = string.Empty;
    public MessageSearchField Field { get; init; }
    public string Value { get; init; } = string.Empty;
    public string FromTime { get; init; } = "00:00";
    public string ToTime { get; init; } = "23:59";
    public bool IranLocalTime { get; init; }
    public MessageSearchCursor? Before { get; init; }

    public SearchMessagesRequest Normalize() => this with
    {
        FromJalali = FromJalali.ToEnglishNumbers().Trim(),
        ToJalali = ToJalali.ToEnglishNumbers().Trim(),
        Value = NormalizeValue(),
        FromTime = FromTime.ToEnglishNumbers().Trim(),
        ToTime = ToTime.ToEnglishNumbers().Trim(),
    };

    private string NormalizeValue()
    {
        string value = Value.ToEnglishNumbers().Trim();
        if (Field != MessageSearchField.MobileNumber) return value;
        if (value.StartsWith('+') && value.Length > 1 && value[1..].All(char.IsAsciiDigit)) value = value[1..];
        return IranMobileNumber.Normalize(value);
    }

    public Result Validate()
    {
        if (!IsDate(FromJalali) || !IsDate(ToJalali))
            return Error.Validation("reports.search_date_invalid", "تاریخ شمسی معتبر با قالب ۱۴۰۵/۰۷/۰۱ وارد کنید.");
        if (string.CompareOrdinal(FromJalali, ToJalali) > 0)
            return Error.Validation("reports.search_range_invalid", "تاریخ شروع باید پیش از پایان باشد.");
        if (!TimeOnly.TryParseExact(FromTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly _) ||
            !TimeOnly.TryParseExact(ToTime, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly _) ||
            (FromJalali == ToJalali && string.CompareOrdinal(FromTime, ToTime) > 0))
            return Error.Validation("reports.search_time_invalid", "بازه ساعت معتبر با قالب ۱۲:۳۰ وارد کنید؛ شروع باید پیش از پایان باشد.");
        if (!Enum.IsDefined(Field))
            return Error.Validation("reports.search_field_invalid", "نوع جست‌وجو معتبر نیست.");
        int maximumLength = Field == MessageSearchField.MobileNumber ? 15 : 31;
        string digits = Field == MessageSearchField.MobileNumber && Value.StartsWith('+') ? Value[1..] : Value;
        if (Value.Length > maximumLength || (Field == MessageSearchField.MobileNumber
                ? digits.Any(character => !char.IsAsciiDigit(character))
                : Value.Any(character => !char.IsAscii(character) || char.IsControl(character))))
            return Error.Validation("reports.search_value_invalid", "شناسه یا شماره تلفن معتبر با طول مجاز وارد کنید.");
        if (Value == "+")
            return Error.Validation("reports.search_value_invalid", "شماره تلفن معتبر وارد کنید.");
        if (Before is not null && (Before.Id <= 0 || !IsDate(Before.SubmitDateJalali) ||
            string.CompareOrdinal(Before.SubmitDateJalali, UtcRange().From.ToPersianDateTimeString("yyyy/MM/dd", false)) < 0 ||
            string.CompareOrdinal(Before.SubmitDateJalali, UtcRange().Until.AddTicks(-1).ToPersianDateTimeString("yyyy/MM/dd", false)) > 0))
            return Error.Validation("reports.search_cursor_invalid", "صفحهٔ جست‌وجو معتبر نیست؛ دوباره جست‌وجو کنید.");
        return Result.Success();
    }

    // The final entered minute is inclusive. Convert before querying; never apply functions to fact columns.
    public (DateTime From, DateTime Until) UtcRange()
    {
        PersianCalendar calendar = new();
        DateTime Parse(string date, string time) => calendar.ToDateTime(
            int.Parse(date.AsSpan(0, 4), CultureInfo.InvariantCulture), int.Parse(date.AsSpan(5, 2), CultureInfo.InvariantCulture),
            int.Parse(date.AsSpan(8, 2), CultureInfo.InvariantCulture), int.Parse(time.AsSpan(0, 2), CultureInfo.InvariantCulture),
            int.Parse(time.AsSpan(3, 2), CultureInfo.InvariantCulture), 0, 0);
        DateTime from = Parse(FromJalali, FromTime);
        DateTime until = Parse(ToJalali, ToTime).AddMinutes(1);
        return IranLocalTime ? (IranTime.ToUtc(from), IranTime.ToUtc(until))
            : (DateTime.SpecifyKind(from, DateTimeKind.Utc), DateTime.SpecifyKind(until, DateTimeKind.Utc));
    }

    private static bool IsDate(string text)
    {
        if (text.Length != 10 || text[4] != '/' || text[7] != '/' || text.Where((character, index) => index != 4 && index != 7).Any(character => !char.IsAsciiDigit(character)))
            return false;
        try
        {
            PersianCalendar calendar = new();
            _ = calendar.ToDateTime(int.Parse(text.AsSpan(0, 4), CultureInfo.InvariantCulture),
                int.Parse(text.AsSpan(5, 2), CultureInfo.InvariantCulture), int.Parse(text.AsSpan(8, 2), CultureInfo.InvariantCulture), 0, 0, 0, 0);
            return true;
        }
        catch (ArgumentOutOfRangeException) { return false; }
    }
}

public sealed record MessageSearchRow(long Id, long MessageBatchId, string SubmitDateJalali, DateTime SubmittedAtUtc,
    string MobileNumber, string? BillId, string? PayId, string CustomerName, string ProviderName,
    SendStatus Status, DeliveryStatus DeliveryStatus, decimal TotalCost, string? Body, string? SenderLine = null);

public sealed record MessageSearchPage(IReadOnlyList<MessageSearchRow> Items, MessageSearchCursor? Next);
