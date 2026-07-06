namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// Connection settings for the Magfa HTTP v2 provider, bound from the <c>Providers:Magfa</c>
/// configuration section. Account authentication data lives in dbo.ProviderAccount.
/// </summary>
public sealed class MagfaOptions
{
    public const string SectionName = "Providers:Magfa";

    /// <summary>Magfa's hard cap on messages per <c>/send</c> and mids per <c>/statuses</c> request.</summary>
    public const int MaxMessagesPerRequest = 100;

    /// <summary>
    /// When false, the loopback provider stays registered so dev/local runs and tests work
    /// credential-free.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>Service base URL. The HTTP v2 methods live under <c>/api/http/sms/v2</c>.</summary>
    public string BaseUrl { get; init; } = "https://sms.magfa.com";

    /// <summary>Per-request HTTP timeout. A timeout surfaces as a transient dispatch failure.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum messages per <c>/send</c> request, capped at <see cref="MaxMessagesPerRequest"/>.
    /// </summary>
    public int BatchSize { get; init; } = MaxMessagesPerRequest;

    public void Validate()
    {
        if (!Enabled)
            return;

        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException($"{SectionName}:BaseUrl is required when Magfa is enabled.");

        if (BatchSize is < 1 or > MaxMessagesPerRequest)
            throw new InvalidOperationException(
                $"{SectionName}:BatchSize must be between 1 and {MaxMessagesPerRequest} (Magfa's per-request limit).");
    }
}
