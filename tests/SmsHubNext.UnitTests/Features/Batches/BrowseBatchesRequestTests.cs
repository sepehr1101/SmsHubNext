using SmsHubNext.Features.Batches;
using SmsHubNext.Shared.Time;
using SmsHubNext.Shared.Ui;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Batches;

public sealed class BrowseBatchesRequestTests
{
    [Fact]
    public void Tehran_midnight_uses_previous_utc_day_and_correct_jalali_partition()
    {
        Assert.True(PersianDisplay.TryParseDateTime("۱۴۰۵/۰۷/۲۶ ۰۰:۰۰", out DateTime? wall));
        DateTime utc = IranTime.ToUtc(wall!.Value);
        Assert.Equal(20, utc.Hour);
        Assert.Equal(30, utc.Minute);
        Assert.Equal("1405/07/26", JalaliDate.FromUtc(utc));
        Assert.Equal(wall.Value.Ticks, IranTime.FromUtc(new DateTimeOffset(utc)).Ticks);
        Assert.True(new BrowseBatchesRequest(utc, utc.AddDays(1)).Validate().IsSuccess);
    }

    [Fact]
    public void Rejects_reversed_empty_and_non_utc_ranges()
    {
        DateTime start = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(new BrowseBatchesRequest(start, start).Validate().IsFailure);
        Assert.True(new BrowseBatchesRequest(start, start.AddMinutes(-1)).Validate().IsFailure);
        Assert.True(new BrowseBatchesRequest(DateTime.SpecifyKind(start, DateTimeKind.Local), start.AddDays(1)).Validate().IsFailure);
    }

    [Fact]
    public void Cursor_must_be_positive_and_within_the_requested_period()
    {
        DateTime start = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
        string date = JalaliDate.FromUtc(start);
        Assert.True(new BrowseBatchesRequest(start, start.AddHours(1), new(date, 25)).Validate().IsSuccess);
        Assert.True(new BrowseBatchesRequest(start, start.AddHours(1), new(date, 0)).Validate().IsFailure);
        Assert.True(new BrowseBatchesRequest(start, start.AddHours(1), new("1400/01/01", 1)).Validate().IsFailure);
    }
}
