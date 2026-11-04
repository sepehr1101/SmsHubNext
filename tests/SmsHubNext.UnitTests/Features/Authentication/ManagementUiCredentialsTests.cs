using Microsoft.AspNetCore.Identity;
using SmsHubNext.Features.Authentication.Ui;
using SmsHubNext.Shared.Ui;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Authentication;

public sealed class ManagementUiCredentialsTests
{
    [Fact]
    public void Production_never_accepts_the_development_account_without_configuration()
    {
        Assert.False(ManagementUiCredentials.AreValid(
            DevelopmentAccount.UserName, DevelopmentAccount.Password, new ManagementUiOptions(), false));
        Assert.True(ManagementUiCredentials.AreValid(
            DevelopmentAccount.UserName, DevelopmentAccount.Password, new ManagementUiOptions(), true));
    }

    [Fact]
    public void Configured_hash_controls_login_in_all_environments()
    {
        ManagementUiOptions options = new() { LoginUserName = "Operator" };
        options.LoginPasswordHash = new PasswordHasher<ManagementUiOptions>()
            .HashPassword(options, "test-only-password");

        Assert.True(ManagementUiCredentials.AreValid(" operator ", "test-only-password", options, false));
        Assert.False(ManagementUiCredentials.AreValid("Operator", "wrong-password", options, false));
        Assert.False(ManagementUiCredentials.AreValid("other", "test-only-password", options, false));
        Assert.False(ManagementUiCredentials.AreValid(
            DevelopmentAccount.UserName, DevelopmentAccount.Password, options, true));
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("")]
    [InlineData("AQ==")]
    public void Missing_or_invalid_hash_fails_closed(string hash)
    {
        ManagementUiOptions options = new() { LoginUserName = "Operator", LoginPasswordHash = hash };
        Assert.False(ManagementUiCredentials.AreValid("Operator", "password", options, false));
    }
}
