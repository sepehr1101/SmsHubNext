using SmsHubNext.Features.Dispatch;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Dispatch;

public sealed class SendRetryPolicyTests
{
    private static readonly DateTime First = new(2026, 10, 19, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public void Initial_send_and_three_retries_are_allowed(int attempts, bool held) =>
        Assert.Equal(held, SendRetryPolicy.MustHold(attempts, First, First.AddHours(1), null));

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    public void Uid_window_excludes_the_24_hour_boundary(int secondsFromExpiry, bool held) =>
        Assert.Equal(held, SendRetryPolicy.MustHold(1, First,
            First.AddHours(24).AddSeconds(secondsFromExpiry), TimeSpan.FromHours(24)));

    [Fact]
    public void Other_providers_do_not_inherit_the_4040_window() =>
        Assert.False(SendRetryPolicy.MustHold(1, First, First.AddDays(2), null));
}
