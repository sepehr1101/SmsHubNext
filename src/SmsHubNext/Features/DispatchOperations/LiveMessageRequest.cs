using System.Globalization;
using System.Text.RegularExpressions;
using DNTPersianUtils.Core;
using SmsHubNext.Shared.Sms;

namespace SmsHubNext.Features.DispatchOperations;

public sealed record LiveMessageCursor(string SubmitDateJalali, long Id)
{
    public bool IsValid => Id > 0 && Regex.IsMatch(SubmitDateJalali,
        @"^[0-9]{4}/(0[1-9]|1[0-2])/(0[1-9]|[12][0-9]|3[01])$");
}

public sealed record LiveMessageRequest
{
    public int Hours { get; init; } = 24;
    public byte? Status { get; init; }
    public byte? Provider { get; init; }
    public long? Batch { get; init; }
    public string Search { get; init; } = "";
    public int PageSize { get; init; } = 25;
    public DateTime? UntilUtc { get; init; }
    public LiveMessageCursor? Before { get; init; }
}

public sealed record LiveMessageSearch(bool HasSearch, string? MobileNumber, long? MessageId)
{
    public bool IsValid => !HasSearch || MobileNumber is not null || MessageId is not null;

    public static LiveMessageSearch Parse(string search)
    {
        string normalized = search.ToEnglishNumbers().Trim();
        if (normalized.Length == 0) return new(false, null, null);
        if (normalized.Length > 64) return new(true, null, null);

        long? id = long.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out long value) && value > 0
            ? value : null;
        string phone = Regex.Replace(normalized, @"[\s()\-]", "");
        if (phone.StartsWith("+98", StringComparison.Ordinal)) phone = phone[1..];
        else if (phone.StartsWith("0098", StringComparison.Ordinal)) phone = phone[2..];
        phone = IranMobileNumber.Normalize(phone);
        return new(true, Regex.IsMatch(phone, @"^989[0-9]{9}$") ? phone : null, id);
    }
}
