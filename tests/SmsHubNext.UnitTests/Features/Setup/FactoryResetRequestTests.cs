using SmsHubNext.Features.Setup;
using SmsHubNext.Shared.Results;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Setup;

public sealed class FactoryResetRequestTests
{
    [Fact]
    public void Accepts_the_exact_reset_confirmation()
    {
        FactoryResetRequest request = new FactoryResetRequest { Confirmation = "RESET" };

        Assert.True(request.Validate().IsSuccess);
    }

    [Theory]
    [InlineData("")]
    [InlineData("reset")]
    [InlineData(" RESET ")]
    public void Rejects_missing_or_inexact_confirmation(string confirmation)
    {
        FactoryResetRequest request = new FactoryResetRequest { Confirmation = confirmation };

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("setup.factory_reset_confirmation_required", result.Error!.Code);
    }
}
