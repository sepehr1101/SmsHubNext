namespace SmsHubNext.Features.Administration;

public sealed record ApplicationError(DateTimeOffset Timestamp, string Level, string Message, string Source, string? Exception, string? TraceId);
public sealed record ApplicationErrorResults(IReadOnlyList<ApplicationError> Items, bool Truncated, int SkippedLines);

public static class ApplicationLogTime
{
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    public static DateTime ToTehran(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Tehran).DateTime;
    public static DateTime ToUtc(DateTime wallTime) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified), Tehran);
}
