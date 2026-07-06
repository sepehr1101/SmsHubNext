using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ProviderAccounts;

public sealed class UpdateProviderAccountRequest
{
    public string ProviderCode { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string AuthType { get; init; } = string.Empty;
    public IReadOnlyDictionary<string, string> Settings { get; init; } = new Dictionary<string, string>();
    public string? Secret { get; init; }
    public bool IsActive { get; init; } = true;

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(ProviderCode))
            return Error.Validation("provider_accounts.provider_code_required", UserMessages.ProviderAccounts.ProviderCodeRequired);

        if (string.IsNullOrWhiteSpace(DisplayName))
            return Error.Validation("provider_accounts.display_name_required", UserMessages.ProviderAccounts.DisplayNameRequired);

        if (string.IsNullOrWhiteSpace(AuthType))
            return Error.Validation("provider_accounts.auth_type_required", UserMessages.ProviderAccounts.AuthTypeRequired);

        if (Secret is not null && string.IsNullOrWhiteSpace(Secret))
            return Error.Validation("provider_accounts.secret_required", UserMessages.ProviderAccounts.SecretRequired);

        return ProviderAccountSettings.Validate(ProviderCode, AuthType, Settings);
    }
}
