namespace SmsHubNext.Features.Providers.Kavenegar;

/// <summary>
/// Connection settings for the Kavenegar REST provider. Account authentication data lives in
/// dbo.ProviderAccount.
/// </summary>
public sealed class KavenegarOptions
{
    public const string SectionName = "Providers:Kavenegar";
    public const int MaxMessagesPerRequest = 200;
    public const int MaxStatusesPerRequest = 500;

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "https://api.kavenegar.com";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public int BatchSize { get; init; } = MaxMessagesPerRequest;

    public void Validate()
    {
        if (!Enabled)
            return;

        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException($"{SectionName}:BaseUrl is required when Kavenegar is enabled.");

        if (BatchSize is < 1 or > MaxMessagesPerRequest)
            throw new InvalidOperationException(
                $"{SectionName}:BatchSize must be between 1 and {MaxMessagesPerRequest}.");
    }
}
