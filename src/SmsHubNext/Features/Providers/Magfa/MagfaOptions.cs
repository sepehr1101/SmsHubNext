namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// Connection settings for the Magfa HTTP v2 provider, bound from the <c>Providers:Magfa</c>
/// configuration section. Secrets (<see cref="Password"/>) come from user-secrets / environment
/// variables — never committed appsettings. Encrypted <c>ProviderCredential</c> storage is a
/// later roadmap phase; config is sufficient for Phase 1.
/// </summary>
public sealed class MagfaOptions
{
    public const string SectionName = "Providers:Magfa";

    /// <summary>Magfa's hard cap on messages per <c>/send</c> (and mids per <c>/statuses</c>) request (reference §1).</summary>
    public const int MaxMessagesPerRequest = 100;

    /// <summary>
    /// When false (the default), the loopback provider stays registered so dev/local runs and
    /// the dispatch tests work without Magfa credentials. Set true once credentials are present.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>Service base URL. The HTTP v2 methods live under <c>/api/http/sms/v2</c>.</summary>
    public string BaseUrl { get; init; } = "https://sms.magfa.com";

    /// <summary>Account username (the part before the <c>/</c> in the Basic-auth user field).</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>Account domain (the part after the <c>/</c> in the Basic-auth user field).</summary>
    public string Domain { get; init; } = string.Empty;

    /// <summary>Service password (distinct from the panel login password).</summary>
    public string Password { get; init; } = string.Empty;

    /// <summary>Per-request HTTP timeout. A timeout surfaces as a transient dispatch failure.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum messages per <c>/send</c> request, capped at <see cref="MaxMessagesPerRequest"/>; the
    /// dispatcher chunks a batch by this value, so one HTTP request carries up to this many messages.
    /// </summary>
    public int BatchSize { get; init; } = MaxMessagesPerRequest;

    /// <summary>The Basic-auth user field: <c>USERNAME/DOMAIN</c> (see the API reference, §3).</summary>
    public string BasicAuthUser => $"{Username}/{Domain}";

    /// <summary>
    /// Fail-fast guard for when the provider is enabled: all three credential parts and a base
    /// URL must be present, otherwise startup should not proceed with a half-configured provider.
    /// </summary>
    public void Validate()
    {
        if (!Enabled)
            return;

        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException($"{SectionName}:BaseUrl is required when Magfa is enabled.");
        if (string.IsNullOrWhiteSpace(Username))
            throw new InvalidOperationException($"{SectionName}:Username is required when Magfa is enabled.");
        if (string.IsNullOrWhiteSpace(Domain))
            throw new InvalidOperationException($"{SectionName}:Domain is required when Magfa is enabled.");
        if (string.IsNullOrWhiteSpace(Password))
            throw new InvalidOperationException($"{SectionName}:Password is required when Magfa is enabled.");
        if (BatchSize is < 1 or > MaxMessagesPerRequest)
            throw new InvalidOperationException(
                $"{SectionName}:BatchSize must be between 1 and {MaxMessagesPerRequest} (Magfa's per-request limit).");
    }
}
