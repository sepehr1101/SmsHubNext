using System.Globalization;

namespace SmsHubNext.Shared.Time;

/// <summary>
/// Formats a UTC instant as the Jalali (Persian) calendar date string that is the
/// partition + reporting period key across the fact tables (README §1, §4.10):
/// <c>yyyy/MM/dd</c> — e.g. <c>1405/01/03</c>.
/// </summary>
/// <remarks>
/// The calendar day is the Iran-local one, derived with a constant UTC+03:30 offset:
/// Iran Standard Time has no DST (abolished in 2022), so a fixed offset is exact and
/// avoids depending on the host's time-zone database. Revisit only if pre-2022
/// historical instants must be re-derived.
/// </remarks>
public static class JalaliDate
{
    private static readonly TimeSpan IranOffset = new(3, 30, 0);
    private static readonly PersianCalendar Calendar = new();

    /// <summary>The Jalali date (Iran local) for a UTC instant, as <c>yyyy/MM/dd</c>.</summary>
    public static string FromUtc(DateTime utc)
    {
        DateTime iranLocal = DateTime.SpecifyKind(utc, DateTimeKind.Unspecified) + IranOffset;
        int year = Calendar.GetYear(iranLocal);
        int month = Calendar.GetMonth(iranLocal);
        int day = Calendar.GetDayOfMonth(iranLocal);
        return string.Create(CultureInfo.InvariantCulture, $"{year:D4}/{month:D2}/{day:D2}");
    }
}
