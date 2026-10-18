using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Authentication;

public sealed class BearerTokenOptions
{
    public const string SectionName = "BearerTokens";

    public string Key { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int AccessTokenExpirationMinutes { get; init; }
    public int RefreshTokenExpirationMinutes { get; init; }
    public bool AllowMultipleLoginsFromTheSameUser { get; init; }
    public bool AllowSignoutAllUserActiveClients { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Key))
            throw new InvalidOperationException($"{SectionName}:Key is required.");

        if (System.Text.Encoding.UTF8.GetByteCount(Key) < 32)
            throw new InvalidOperationException($"{SectionName}:Key must contain at least 32 bytes.");

        if (string.IsNullOrWhiteSpace(Issuer))
            throw new InvalidOperationException($"{SectionName}:Issuer is required.");

        if (string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException($"{SectionName}:Audience is required.");

        if (AccessTokenExpirationMinutes is < 5 or > 30)
        {
            throw new InvalidOperationException(
                $"{SectionName}:AccessTokenExpirationMinutes must be between 5 and 30.");
        }

        if (RefreshTokenExpirationMinutes <= 0)
            throw new InvalidOperationException($"{SectionName}:RefreshTokenExpirationMinutes must be positive.");
    }
}
