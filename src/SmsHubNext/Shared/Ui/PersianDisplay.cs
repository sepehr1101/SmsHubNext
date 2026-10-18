using System.Globalization;
using DNTPersianUtils.Core;

namespace SmsHubNext.Shared.Ui;

/// <summary>Presentation only: dates keep their stored time zone and values stay unchanged.</summary>
public static class PersianDisplay
{
    public static string Text(string? value) => (value ?? "—").ToPersianNumbers();

    public static string Number(object? value, string? format = null)
    {
        if (value is null) return "—";
        string text = value is IFormattable number
            ? number.ToString(format, CultureInfo.InvariantCulture)
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return Text(text).Replace(',', '٬').Replace('.', '٫');
    }

    public static string Date(DateTime? value, string format = "yyyy/MM/dd") =>
        value is null ? "—" : Text(value.Value.ToPersianDateTimeString(format, convertToIranTimeZone: false));

    public static string ProviderDate(string? value) =>
        DateTime.TryParseExact(value, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date)
            ? Date(date, "yyyy/MM/dd HH:mm:ss")
            : Text(value);
    public static bool TryParseDateTime(string text, out DateTime? value)
    {
        string normalized = SearchText(text);
        value = null;
        if (normalized.Length == 0) return true;
        if (!System.Text.RegularExpressions.Regex.IsMatch(normalized, @"^[0-9]{4}/[0-9]{2}/[0-9]{2} [0-9]{2}:[0-9]{2}$"))
            return false;
        int hour = int.Parse(normalized.AsSpan(11, 2), CultureInfo.InvariantCulture);
        int minute = int.Parse(normalized.AsSpan(14, 2), CultureInfo.InvariantCulture);
        if (hour > 23 || minute > 59) return false;
        DateTime? parsed = normalized.ToGregorianDateTime(convertToUtc: false);
        if (parsed is null) return false;
        value = DateTime.SpecifyKind(parsed.Value, DateTimeKind.Utc);
        return true;
    }

    public static string SearchText(string text) =>
        text.ToEnglishNumbers().Trim().Replace('ي', 'ی').Replace('ك', 'ک');
}