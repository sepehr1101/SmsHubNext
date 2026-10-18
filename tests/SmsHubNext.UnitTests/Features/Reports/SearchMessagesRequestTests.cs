using SmsHubNext.Features.Reports;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Reports;

public sealed class SearchMessagesRequestTests
{
    [Fact]
    public void Iran_midnight_search_includes_previous_utc_partition_and_final_minute()
    {
        SearchMessagesRequest query = (Valid() with
        {
            FromJalali = "۱۴۰۵/۰۷/۰۱", ToJalali = "۱۴۰۵/۰۷/۰۱",
            FromTime = "۰۰:۰۰", ToTime = "۰۰:۳۰", IranLocalTime = true,
        }).Normalize();
        Assert.True(query.Validate().IsSuccess);
        (DateTime from, DateTime until) = query.UtcRange();
        Assert.Equal(new DateTime(2026, 9, 22, 20, 30, 0, DateTimeKind.Utc), from);
        Assert.Equal(new DateTime(2026, 9, 22, 21, 1, 0, DateTimeKind.Utc), until);
        Assert.True((query with { Before = new("1405/06/31", 123) }).Validate().IsSuccess);
        Assert.True((query with { Before = new("1405/07/01", 123) }).Validate().IsFailure);
    }

    [Theory]
    [InlineData("24:00", "23:59")]
    [InlineData("12:60", "23:59")]
    [InlineData("12:00", "11:59")]
    [InlineData("", "23:59")]
    public void Rejects_invalid_or_reversed_time_range(string from, string until) =>
        Assert.True((Valid() with { ToJalali = "1405/07/01", FromTime = from, ToTime = until }).Validate().IsFailure);

    [Fact]
    public void Normalizes_persian_and_arabic_digits_without_losing_leading_zeroes()
    {
        SearchMessagesRequest request = new SearchMessagesRequest
        {
            FromJalali = " ۱۴۰۵/۰۷/۰۱ ", ToJalali = "١٤٠٥/٠٧/٣٠", Value = " ۰۰۱۲۳ ",
        }.Normalize();
        Assert.Equal("1405/07/01", request.FromJalali);
        Assert.Equal("1405/07/30", request.ToJalali);
        Assert.Equal("00123", request.Value);
        Assert.True(request.Validate().IsSuccess);
    }

    [Theory]
    [InlineData("1405/07/31", "1405/08/01")]
    [InlineData("1405/13/01", "1405/13/02")]
    [InlineData("1405/01/01", "1405/12/30")]
    [InlineData("1405-01-01", "1405/01/02")]
    [InlineData("1405/02/02", "1405/02/01")]
    public void Rejects_invalid_or_reversed_jalali_ranges(string from, string to) =>
        Assert.True((Valid() with { FromJalali = from, ToJalali = to }).Validate().IsFailure);

    [Theory]
    [InlineData(MessageSearchField.BillId, "bill-001", true)]
    [InlineData(MessageSearchField.PayId, "001234567890", true)]
    [InlineData(MessageSearchField.MobileNumber, "+989120000001", true)]
    [InlineData(MessageSearchField.MobileNumber, "+98+912", false)]
    [InlineData(MessageSearchField.MobileNumber, "+", false)]
    [InlineData(MessageSearchField.MobileNumber, "0912abc", false)]
    [InlineData(MessageSearchField.BillId, "12345678901234567890123456789012", false)]
    [InlineData(MessageSearchField.MobileNumber, "1234567890123456", false)]
    public void Validates_search_value(MessageSearchField field, string value, bool valid) =>
        Assert.Equal(valid, (Valid() with { Field = field, Value = value }).Validate().IsSuccess);

    [Fact]
    public void Local_and_international_phone_searches_use_the_stored_format()
    {
        foreach (string value in new[] { "۰۹۱۲" + new string('۰', 6) + "۱", "989120000001", "+989120000001" })
        {
            SearchMessagesRequest query = (Valid() with { Field = MessageSearchField.MobileNumber, Value = value }).Normalize();
            Assert.Equal("989120000001", query.Value);
            Assert.True(query.Validate().IsSuccess);
        }
    }

    [Fact]
    public void Supports_date_only_search_and_validates_cursor_and_field()
    {
        Assert.True((Valid() with { Value = string.Empty }).Validate().IsSuccess);
        Assert.True((Valid() with { Field = (MessageSearchField)99 }).Validate().IsFailure);
        Assert.True((Valid() with { Before = new("1405/07/01", 1) }).Validate().IsSuccess);
        Assert.True((Valid() with { Before = new("1405/06/31", 1) }).Validate().IsFailure);
        Assert.True((Valid() with { Before = new("1405/07/01", 0) }).Validate().IsFailure);
    }

    private static SearchMessagesRequest Valid() => new() { FromJalali = "1405/07/01", ToJalali = "1405/07/30", Value = "123" };
}
