namespace SmsHubNext.Shared.Ui;

public static class IranTime
{
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    public static DateTime FromUtc(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Tehran).DateTime;
    public static DateTime ToUtc(DateTime wallTime) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(wallTime, DateTimeKind.Unspecified), Tehran);
}
