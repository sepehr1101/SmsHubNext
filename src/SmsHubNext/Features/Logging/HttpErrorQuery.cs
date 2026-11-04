using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Logging;

public sealed class HttpErrorQuery
{
    public string StatusClass { get; init; } = string.Empty;
    public DateOnly FromDate { get; init; }
    public DateOnly ToDate { get; init; }
    public TimeOnly? FromTime { get; init; }
    public TimeOnly? ToTime { get; init; }
    public int? StatusCode { get; init; }

    public Result Validate()
    {
        if (!string.Equals(StatusClass, "4xx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(StatusClass, "5xx", StringComparison.OrdinalIgnoreCase))
            return Error.Validation("logging.invalid_status_class", "گروه خطا باید 4xx یا 5xx باشد.");
        if (FromDate == default || ToDate == default || FromDate > ToDate || ToDate.DayNumber - FromDate.DayNumber > 30)
            return Error.Validation("logging.invalid_dates", "بازه تاریخ باید معتبر و حداکثر شامل ۳۱ روز باشد.");
        if (FromTime.HasValue != ToTime.HasValue)
            return Error.Validation("logging.invalid_hours", "ساعت شروع و پایان را با هم وارد کنید.");
        if (StatusCode.HasValue && (StatusCode < 400 || StatusCode > 599 || StatusCode / 100 != StatusGroup))
            return Error.Validation("logging.invalid_status_code", "کد خطا باید در گروه انتخاب‌شده باشد.");
        return Result.Success();
    }

    internal int StatusGroup => StatusClass.StartsWith('4') ? 4 : 5;

    internal bool Matches(DateTime localTime, int statusCode)
    {
        DateOnly date = DateOnly.FromDateTime(localTime);
        if (date < FromDate || date > ToDate || statusCode / 100 != StatusGroup ||
            (StatusCode.HasValue && StatusCode.Value != statusCode)) return false;
        if (!FromTime.HasValue) return true;
        TimeOnly time = TimeOnly.FromDateTime(localTime);
        return FromTime <= ToTime
            ? time >= FromTime.Value && time <= ToTime!.Value
            : time >= FromTime.Value || time <= ToTime!.Value;
    }
}

public sealed record HttpErrorEntry(DateTimeOffset Timestamp, int StatusCode, string Method, string Path, string TraceId);
public sealed record HttpErrorResults(IReadOnlyList<HttpErrorEntry> Items, bool Truncated, int SkippedLines);
