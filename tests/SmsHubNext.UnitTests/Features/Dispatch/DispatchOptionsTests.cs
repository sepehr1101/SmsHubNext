using SmsHubNext.Features.Dispatch;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Dispatch;

public sealed class DispatchOptionsTests
{
    [Fact]
    public void Defaults_are_safe()
    {
        new DispatchOptions().Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Fewer_than_two_negative_confirmations_is_rejected(int count)
    {
        DispatchOptions options = new DispatchOptions { RequiredNegativeConfirmations = count };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("RequiredNegativeConfirmations", exception.Message);
    }

    [Fact]
    public void Awaiting_confirmation_window_must_stay_below_provider_lookup_retention()
    {
        DispatchOptions options = new DispatchOptions
        {
            AwaitingConfirmationMaxAge = TimeSpan.FromHours(12),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("12-hour", exception.Message);
    }

    [Fact]
    public void Dispatch_lease_must_cover_a_reasonable_provider_request_window()
    {
        DispatchOptions options = new DispatchOptions
        {
            DispatchLeaseTimeout = TimeSpan.FromSeconds(59),
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("at least one minute", exception.Message);
    }
}
