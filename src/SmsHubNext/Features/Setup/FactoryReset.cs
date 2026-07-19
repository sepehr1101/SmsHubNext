using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Setup;

public sealed class FactoryResetRequest
{
    public const string RequiredConfirmation = "RESET";

    public string Confirmation { get; init; } = string.Empty;

    public Result Validate() => string.Equals(
        Confirmation,
        RequiredConfirmation,
        StringComparison.Ordinal)
        ? Result.Success()
        : Error.Validation(
            "setup.factory_reset_confirmation_required",
            UserMessages.Setup.FactoryResetConfirmationRequired);
}

public sealed record FactoryResetResponse(
    DateTime ResetAtUtc,
    bool RequiresSetupWizard);
