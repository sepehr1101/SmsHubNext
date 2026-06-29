namespace SmsHubNext.Features.Inbound;

/// <summary>Tuning for the inbound (MO) poll worker (bound from the <c>InboundPolling</c> section).</summary>
public sealed class InboundPollOptions
{
    public const string SectionName = "InboundPolling";

    /// <summary>
    /// Inbound polling is destructive (it dequeues messages at the provider) and account-specific, so
    /// it is opt-in: the worker is only hosted when this is true.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>How long to idle when the inbox was empty before pulling again.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How many messages to pull per request (providers cap this — Magfa at 100).</summary>
    public int BatchSize { get; init; } = 50;
}
