using SmsHubNext.Features.Authentication;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Authentication;

public sealed class BearerTokenOptionsTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(31)]
    public void Administrative_access_token_lifetime_is_bounded(int minutes)
    {
        BearerTokenOptions options = ValidOptions(minutes);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(options.Validate);

        Assert.Contains("between 5 and 30", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    public void Administrative_access_token_accepts_short_lifetimes(int minutes)
    {
        BearerTokenOptions options = ValidOptions(minutes);

        options.Validate();
    }

    private static BearerTokenOptions ValidOptions(int accessTokenExpirationMinutes) => new()
    {
        Key = new string('k', 64),
        Issuer = "tests",
        Audience = "administrators",
        AccessTokenExpirationMinutes = accessTokenExpirationMinutes,
        RefreshTokenExpirationMinutes = 60,
    };
}
