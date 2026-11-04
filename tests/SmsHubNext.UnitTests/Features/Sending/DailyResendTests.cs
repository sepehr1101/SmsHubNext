using SmsHubNext.Features.Authentication;
using SmsHubNext.Features.Sending;
using SmsHubNext.Shared.Database;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Sending;

public sealed class DailyResendTests
{
    private static readonly DateTime Now = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("۱۴۰۵/۰۸/۱۰", "1405/08/10")]
    [InlineData("١٤٠٥/٠٨/٠٩", "1405/08/09")]
    [InlineData(" 1405/08/09 ", "1405/08/09")]
    [InlineData("1403/12/30", "1403/12/30")]
    public void Date_accepts_persian_arabic_and_ascii_digits(string input, string expected) =>
        Assert.Equal(expected, DailyResendDate.Parse(input, Now).Value);

    [Theory]
    [InlineData("1405-08-09")]
    [InlineData("1405/13/01")]
    [InlineData("1405/08/31")]
    [InlineData("1405/08/11")]
    [InlineData("1403x01x01")]
    [InlineData("")]
    [InlineData("1404/12/30")]
    public void Invalid_or_future_dates_are_rejected(string input) => Assert.True(DailyResendDate.Parse(input, Now).IsFailure);

    [Fact]
    public async Task Customer_cannot_start_confirm_or_read_cross_tenant_day_work()
    {
        DailyResendHandler handler = new(new Db("unused"), TimeProvider.System);
        ApiKeyIdentity customer = new(1, 1, "prefix");
        Assert.Equal("sending.daily_forbidden", (await handler.Begin("1405/08/09", customer, CancellationToken.None)).Error!.Code);
        Assert.Equal("sending.daily_forbidden", (await handler.Confirm(Guid.NewGuid(), customer, CancellationToken.None)).Error!.Code);
        Assert.Equal("sending.daily_forbidden", (await handler.Status(Guid.NewGuid(), customer, CancellationToken.None)).Error!.Code);
    }

    [Theory]
    [InlineData(29, false)]
    [InlineData(30, true)]
    public void Today_boundary_uses_iran_midnight_instead_of_server_time(int minute, bool allowed)
    {
        DateTime utc = new(2026, 11, 1, 20, minute, 0, DateTimeKind.Utc);
        Assert.Equal(allowed, DailyResendDate.Parse("1405/08/11", utc).IsSuccess);
    }
}
