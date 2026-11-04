using SmsHubNext.Features.Reports;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Reports;

public sealed class DimensionReportRequestTests
{
    [Theory]
    [InlineData("1405/13/01", "1405/13/02")]
    [InlineData("1405/12/30", "1405/12/30")]
    [InlineData("1405/02/01", "1405/01/01")]
    [InlineData("۱۴۰۵/۰۱/۰۱", "۱۴۰۵/۰۱/۰۲")]
    public void Rejects_invalid_calendar_dates_and_ranges(string from, string to) => Assert.True(new DimensionReportRequest
    {
        Filters = new() { FromJalali = from, ToJalali = to },
    }.Validate().IsFailure);

    [Theory]
    [InlineData("1400/01/01", "1405/01/31", true)]
    [InlineData("1400/01/01", "1405/02/01", false)]
    [InlineData("9378/10/01", "9378/10/13", true)]
    public void Limits_by_persian_months_without_overflow(string from, string to, bool valid) =>
        Assert.Equal(valid, new DimensionReportRequest { Filters = new() { FromJalali = from, ToJalali = to } }.Validate().IsSuccess);
    [Fact]
    public void Rejects_undefined_grouping_and_level()
    {
        MessageReportRequest filters = new() { FromJalali = "1405/01/01", ToJalali = "1405/02/31" };
        Assert.True(new DimensionReportRequest { Filters = filters, Kind = (DimensionReportKind)42 }.Validate().IsFailure);
        Assert.True(new DimensionReportRequest { Filters = filters, GeographyLevel = 4 }.Validate().IsFailure);
        Assert.True(new DimensionReportRequest { Filters = filters }.Validate().IsSuccess);
    }
    [Fact]
    public void Delivery_rate_uses_accepted_messages_and_handles_zero()
    {
        Assert.Equal(50m, new DimensionReportRow { MessageCount = 10, AcceptedCount = 4, DeliveredCount = 2 }.DeliveryRate);
        Assert.Equal(0m, new DimensionReportRow { MessageCount = 10 }.DeliveryRate);
    }
}

