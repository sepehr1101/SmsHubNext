using System.Globalization;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Sending;

public enum DailyResendState : byte { Preparing = 0, Ready = 1, Running = 2, Completed = 3, Paused = 4 }

public static class DailyResendDate
{
    public static Result<string> Parse(string input, DateTime nowUtc)
    {
        string text = PersianDisplay.SearchText(input);
        if (text.Length != 10 || text[4] != '/' || text[7] != '/' ||
            text.Where((character, index) => index != 4 && index != 7).Any(character => character is < '0' or > '9'))
            return Invalid();
        try
        {
            PersianCalendar calendar = new();
            DateTime day = calendar.ToDateTime(int.Parse(text.AsSpan(0, 4), CultureInfo.InvariantCulture),
                int.Parse(text.AsSpan(5, 2), CultureInfo.InvariantCulture), int.Parse(text.AsSpan(8, 2), CultureInfo.InvariantCulture), 0, 0, 0, 0);
            return day.Date <= (nowUtc + TimeSpan.FromMinutes(210)).Date ? text : Invalid();
        }
        catch (ArgumentOutOfRangeException) { return Invalid(); }
    }

    private static Error Invalid() => Error.Validation("sending.daily_invalid_date", "یک تاریخ شمسی معتبر، حداکثر تا امروز، با قالب ۱۴۰۵/۰۸/۱۰ وارد کنید.");
}

public sealed record DailyResendSummary(Guid Id, string SubmitDateJalali, DailyResendState State,
    long Examined, long Eligible, decimal EstimatedCost, long Queued, long Submitted, long Failed,
    long Pending, long Skipped, long MissingHistory, decimal RemainingCost = 0)
{
    public long Remaining => Math.Max(0, Examined - Queued - Skipped);
    public IReadOnlyList<DailyResendSkipReason> SkipReasons { get; init; } = [];
}

public sealed record DailyResendSkipReason(string Code, long Count);
