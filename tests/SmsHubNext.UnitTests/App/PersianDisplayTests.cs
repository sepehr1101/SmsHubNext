using SmsHubNext.Shared.Ui;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class PersianDisplayTests
{
    [Theory]
    [InlineData(2024, 3, 20, "۱۴۰۳/۰۱/۰۱")]
    [InlineData(2025, 3, 20, "۱۴۰۳/۱۲/۳۰")]
    [InlineData(2025, 3, 21, "۱۴۰۴/۰۱/۰۱")]
    public void Gregorian_dates_display_as_jalali_with_persian_digits(int year, int month, int day, string expected)
    {
        Assert.Equal(expected, PersianDisplay.Date(new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Formatting_keeps_utc_time_and_handles_missing_values()
    {
        DateTime utc = new(2024, 3, 20, 23, 45, 12, DateTimeKind.Utc);
        Assert.Equal("۱۴۰۳/۰۱/۰۱ ۲۳:۴۵:۱۲", PersianDisplay.Date(utc, "yyyy/MM/dd HH:mm:ss"));
        Assert.Equal("—", PersianDisplay.Date(null));
        Assert.Equal("—", PersianDisplay.Number(null));
        Assert.Equal("۰۹۱۲۳۴۵۶۷۸۹", PersianDisplay.Text("09123456789"));
    }

    [Fact]
    public void Persian_date_input_round_trips_utc_values_and_accepts_both_digit_sets()
    {
        Assert.True(PersianDisplay.TryParseDateTime("۱۴۰۳/۰۱/۰۱ ۲۳:۴۵", out DateTime? persian));
        Assert.True(PersianDisplay.TryParseDateTime("1403/01/01 23:45", out DateTime? english));
        Assert.Equal(new DateTime(2024, 3, 20, 23, 45, 0, DateTimeKind.Utc), persian);
        Assert.Equal(persian, english);
        Assert.Equal(DateTimeKind.Utc, persian!.Value.Kind);
        Assert.True(PersianDisplay.TryParseDateTime("", out DateTime? empty));
        Assert.Null(empty);
    }

    [Theory]
    [InlineData("۱۴۰۴/۱۲/۳۰ ۱۲:۰۰")]
    [InlineData("۱۴۰۳/۱۳/۰۱ ۱۲:۰۰")]
    [InlineData("۱۴۰۳/۰۱/۰۱ ۲۵:۰۰")]
    [InlineData("2024-03-20")]
    public void Invalid_jalali_input_cannot_be_saved(string text)
    {
        Assert.False(PersianDisplay.TryParseDateTime(text, out DateTime? value));
        Assert.Null(value);
    }
    [Fact]
    public void Numbers_use_persian_digits_and_separators_without_changing_values()
    {
        Assert.Equal("۱٬۲۳۴٫۵", PersianDisplay.Number(1234.5m, "N1"));
        Assert.Equal("۰", PersianDisplay.Number(0));
    }

    [Theory]
    [InlineData(" ۱۴۰۵/۰۷/۰۱ ", "1405/07/01")]
    [InlineData("٠٩١٢٣٤٥٦٧٨٩", "09123456789")]
    [InlineData("كليد ۱۲3", "کلید 123")]
    public void Search_normalizes_persian_arabic_and_english_digits(string input, string expected)
    {
        Assert.Equal(expected, PersianDisplay.SearchText(input));
    }
}