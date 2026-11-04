using Microsoft.AspNetCore.Identity;
using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Authentication.Ui;

public static class ManagementUiCredentials
{
    public static bool AreValid(
        string userName, string password, ManagementUiOptions options, bool isDevelopment)
    {
        if (string.IsNullOrEmpty(options.LoginUserName) && string.IsNullOrEmpty(options.LoginPasswordHash))
        {
            return isDevelopment &&
                string.Equals(userName.Trim(), DevelopmentAccount.UserName, StringComparison.OrdinalIgnoreCase) &&
                password == DevelopmentAccount.Password;
        }

        if (string.IsNullOrWhiteSpace(options.LoginUserName) ||
            string.IsNullOrWhiteSpace(options.LoginPasswordHash) ||
            !string.Equals(userName.Trim(), options.LoginUserName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            PasswordVerificationResult result = new PasswordHasher<ManagementUiOptions>()
                .VerifyHashedPassword(options, options.LoginPasswordHash, password);
            return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
