namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// Connection settings for the Magfa HTTP v2 provider, bound from the <c>Providers:Magfa</c>
/// configuration section. Transport settings (<see cref="BaseUrl"/>/<see cref="Timeout"/>/
/// <see cref="BatchSize"/>) are shared; credentials live per <see cref="MagfaAccount"/> so each
/// sender line can authenticate against its own Magfa account.
///
/// Secrets must not be committed: <c>appsettings.json</c> carries placeholders only, and real
/// credentials go in a gitignored local source (<c>appsettings.{Environment}.local.json</c>) or
/// environment variables. Encrypted <c>ProviderCredential</c> storage is a later roadmap phase;
/// config is sufficient for Phase 1.
/// </summary>
public sealed class MagfaOptions
{
    public const string SectionName = "Providers:Magfa";

    /// <summary>Magfa's hard cap on messages per <c>/send</c> (and mids per <c>/statuses</c>) request (reference §1).</summary>
    public const int MaxMessagesPerRequest = 100;

    /// <summary>
    /// When false (the default), the loopback provider stays registered so dev/local runs and
    /// the dispatch tests work without Magfa credentials. Set true once at least one account is configured.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>Service base URL. The HTTP v2 methods live under <c>/api/http/sms/v2</c>.</summary>
    public string BaseUrl { get; init; } = "https://sms.magfa.com";

    /// <summary>Per-request HTTP timeout. A timeout surfaces as a transient dispatch failure.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum messages per <c>/send</c> request, capped at <see cref="MaxMessagesPerRequest"/>; the
    /// dispatcher chunks a batch by this value, so one HTTP request carries up to this many messages.
    /// </summary>
    public int BatchSize { get; init; } = MaxMessagesPerRequest;

    /// <summary>
    /// The Magfa accounts and the sender lines each one owns. Sending resolves the account from the
    /// message's sender line; at least one account is required when the provider is enabled.
    /// </summary>
    public IReadOnlyList<MagfaAccount> Accounts { get; init; } = [];

    /// <summary>
    /// Fail-fast guard for when the provider is enabled: a base URL, a valid batch size, and at least
    /// one fully-credentialed account that owns at least one sender line — with no sender line claimed
    /// by two accounts — otherwise startup should not proceed with a half-configured provider.
    /// </summary>
    public void Validate()
    {
        if (!Enabled)
            return;

        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException($"{SectionName}:BaseUrl is required when Magfa is enabled.");

        if (BatchSize is < 1 or > MaxMessagesPerRequest)
            throw new InvalidOperationException(
                $"{SectionName}:BatchSize must be between 1 and {MaxMessagesPerRequest} (Magfa's per-request limit).");

        if (Accounts.Count == 0)
            throw new InvalidOperationException($"{SectionName}:Accounts must contain at least one account when Magfa is enabled.");

        HashSet<string> seenLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < Accounts.Count; i++)
        {
            MagfaAccount account = Accounts[i];
            string at = $"{SectionName}:Accounts[{i}]";

            if (string.IsNullOrWhiteSpace(account.Username))
                throw new InvalidOperationException($"{at}:Username is required.");
            if (string.IsNullOrWhiteSpace(account.Domain))
                throw new InvalidOperationException($"{at}:Domain is required.");
            if (string.IsNullOrWhiteSpace(account.Password))
                throw new InvalidOperationException($"{at}:Password is required.");
            if (account.SenderLines.Count == 0)
                throw new InvalidOperationException($"{at}:SenderLines must list at least one sender line.");

            foreach (string line in account.SenderLines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    throw new InvalidOperationException($"{at}:SenderLines contains a blank line number.");
                if (!seenLines.Add(line.Trim()))
                    throw new InvalidOperationException(
                        $"{SectionName}: sender line '{line}' is claimed by more than one account.");
            }
        }
    }
}
