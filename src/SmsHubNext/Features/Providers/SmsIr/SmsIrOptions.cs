namespace SmsHubNext.Features.Providers.SmsIr;

/// <summary>
/// Connection settings for the sms.ir REST provider. API keys live encrypted in
/// <c>dbo.ProviderAccount</c>, not in application configuration.
/// </summary>
public sealed class SmsIrOptions
{
    public const string SectionName = "Providers:SmsIr";
    public const int MaxMessagesPerRequest = 100;
    public const int MaxInboundMessagesPerRequest = 100;

    public bool Enabled { get; init; }

    public string BaseUrl { get; init; } = "https://api.sms.ir";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    public int BatchSize { get; init; } = MaxMessagesPerRequest;

    /// <summary>
    /// Minimum interval between starts of requests made by this adapter. Zero keeps throttling off;
    /// sandbox/load environments should configure a conservative positive value.
    /// </summary>
    public TimeSpan MinimumRequestInterval { get; init; } = TimeSpan.Zero;

    public void Validate()
    {
        if (!Enabled)
            return;

        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException($"{SectionName}:BaseUrl is required when sms.ir is enabled.");

        if (BatchSize is < 1 or > MaxMessagesPerRequest)
        {
            throw new InvalidOperationException(
                $"{SectionName}:BatchSize must be between 1 and {MaxMessagesPerRequest}.");
        }

        if (MinimumRequestInterval < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{SectionName}:MinimumRequestInterval cannot be negative.");
        }
    }
}
