using SmsHubNext.Features.Reports.Ui.Pages;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Reports;

public sealed class ReportPeriodTests
{
    [Theory]
    [InlineData("today", "1405/01/01")]
    [InlineData("month", "1405/01/01")]
    [InlineData("year", "1405/01/01")]
    public void Presets_use_iran_date_at_utc_day_boundary(string preset, string expectedFrom)
    {
        // 20 March 21:00 UTC is already Nowruz in Iran.
        TimeProvider clock = new FixedClock(new DateTimeOffset(2026, 3, 20, 21, 0, 0, TimeSpan.Zero));
        ReportPeriod period = ReportPeriod.Current(clock, preset);
        Assert.Equal(expectedFrom, period.From);
        Assert.Equal("1405/01/01", period.To);
    }
    [Theory]
    [InlineData("today", "1405/08/03")]
    [InlineData("month", "1405/08/01")]
    [InlineData("year", "1405/01/01")]
    public void Presets_keep_their_distinct_start_dates(string preset, string expectedFrom)
    {
        ReportPeriod period = ReportPeriod.Current(new FixedClock(new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero)), preset);
        Assert.Equal(expectedFrom, period.From);
        Assert.Equal("1405/08/03", period.To);
    }
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
