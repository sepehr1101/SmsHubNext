using System.Text.Json;
using SmsHubNext.Features.Providers;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ProviderAccounts;

internal static class ProviderAccountSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string ToJson(IReadOnlyDictionary<string, string> settings) =>
        JsonSerializer.Serialize(settings, JsonOptions);

    public static IReadOnlyDictionary<string, string> FromJson(string settingsJson)
    {
        Dictionary<string, string>? settings =
            JsonSerializer.Deserialize<Dictionary<string, string>>(settingsJson, JsonOptions);

        return settings ?? new Dictionary<string, string>();
    }

    public static Result Validate(string providerCode, string authType, IReadOnlyDictionary<string, string> settings)
    {
        if (string.Equals(providerCode, ProviderCodes.Magfa, StringComparison.OrdinalIgnoreCase))
            return ValidateMagfa(authType, settings);

        if (string.Equals(providerCode, ProviderCodes.Kavenegar, StringComparison.OrdinalIgnoreCase))
            return ValidateKavenegar(authType);

        return Error.Validation("provider_accounts.unsupported_provider", UserMessages.ProviderAccounts.UnsupportedProvider);
    }

    private static Result ValidateMagfa(string authType, IReadOnlyDictionary<string, string> settings)
    {
        if (!string.Equals(authType, ProviderAccountAuthTypes.UsernamePasswordDomain, StringComparison.Ordinal))
            return Error.Validation("provider_accounts.invalid_auth_type", UserMessages.ProviderAccounts.InvalidAuthType);

        if (!settings.TryGetValue("username", out string? username) || string.IsNullOrWhiteSpace(username))
            return Error.Validation("provider_accounts.magfa_username_required", UserMessages.ProviderAccounts.MagfaUsernameRequired);

        if (!settings.TryGetValue("domain", out string? domain) || string.IsNullOrWhiteSpace(domain))
            return Error.Validation("provider_accounts.magfa_domain_required", UserMessages.ProviderAccounts.MagfaDomainRequired);

        return Result.Success();
    }

    private static Result ValidateKavenegar(string authType)
    {
        if (!string.Equals(authType, ProviderAccountAuthTypes.ApiKey, StringComparison.Ordinal))
            return Error.Validation("provider_accounts.invalid_auth_type", UserMessages.ProviderAccounts.InvalidAuthType);

        return Result.Success();
    }
}
