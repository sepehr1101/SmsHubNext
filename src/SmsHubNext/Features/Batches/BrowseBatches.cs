using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Time;

namespace SmsHubNext.Features.Batches;

public sealed record BatchCursor(string SubmitDateJalali, long Id);
public sealed record BrowseBatchesRequest(DateTime FromUtc, DateTime ToUtcExclusive, BatchCursor? Before = null)
{
    public string? Search { get; init; }
    public Result Validate()
    {
        if (Search?.Length > 100)
            return Error.Validation("batches.search_too_long", "جست‌وجو باید حداکثر ۱۰۰ نویسه باشد.");
        if (FromUtc.Kind != DateTimeKind.Utc || ToUtcExclusive.Kind != DateTimeKind.Utc || FromUtc >= ToUtcExclusive)
            return Error.Validation("batches.invalid_range", "بازهٔ تاریخ و ساعت معتبر نیست؛ شروع باید پیش از پایان باشد.");
        try
        {
            string from = JalaliDate.FromUtc(FromUtc);
            string to = JalaliDate.FromUtc(ToUtcExclusive.AddTicks(-1));
            if (Before is not null && (Before.Id <= 0 || Before.SubmitDateJalali.Length != 10 ||
                string.CompareOrdinal(Before.SubmitDateJalali, from) < 0 || string.CompareOrdinal(Before.SubmitDateJalali, to) > 0))
                return Error.Validation("batches.invalid_cursor", "صفحه معتبر نیست؛ دوباره جست‌وجو کنید.");
        }
        catch (ArgumentOutOfRangeException)
        {
            return Error.Validation("batches.invalid_range", "تاریخ در محدودهٔ تقویم شمسی نیست.");
        }
        return Result.Success();
    }
}

public sealed record BatchListItem(long Id, string SubmitDateJalali, DateTime ReceivedAtUtc, string CustomerName,
    string ProviderName, int MessageCount, int SegmentCount, decimal TotalCost, BatchStatus Status);
public sealed record BatchListPage(IReadOnlyList<BatchListItem> Items, BatchCursor? Next);
public sealed record BatchMessagesPage(IReadOnlyList<BatchMessage> Items, long? NextAfterId);
