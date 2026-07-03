using SmsHubNext.Features.Reports;
using SmsHubNext.Shared.Results;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Reports;

public sealed class MessageReportRequestTests
{
    [Fact]
    public void Valid_period_passes()
    {
        MessageReportRequest request = new MessageReportRequest
        {
            FromJalali = "1405/01/01",
            ToJalali = "1405/01/31",
        };

        Assert.True(request.Validate().IsSuccess);
    }

    [Fact]
    public void Rejects_invalid_from_format()
    {
        MessageReportRequest request = new MessageReportRequest
        {
            FromJalali = "1405-01-01",
            ToJalali = "1405/01/31",
        };

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("reports.from_jalali_invalid", result.Error!.Code);
    }

    [Fact]
    public void Rejects_reversed_range()
    {
        MessageReportRequest request = new MessageReportRequest
        {
            FromJalali = "1405/02/01",
            ToJalali = "1405/01/31",
        };

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("reports.invalid_range", result.Error!.Code);
    }

    [Fact]
    public void Rejects_non_positive_optional_filters()
    {
        MessageReportRequest request = new MessageReportRequest
        {
            FromJalali = "1405/01/01",
            ToJalali = "1405/01/31",
            CustomerId = 0,
        };

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("reports.customer_invalid", result.Error!.Code);
    }
}
