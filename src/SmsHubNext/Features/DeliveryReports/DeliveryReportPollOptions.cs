namespace SmsHubNext.Features.DeliveryReports;

/// <summary>Tuning for the delivery-report poll worker (bound from the <c>DeliveryReportPolling</c> section).</summary>
public sealed class DeliveryReportPollOptions
{
    public const string SectionName = "DeliveryReportPolling";

    /// <summary>How long to idle when no poll row is due before checking again.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long after a poll a still-in-flight message waits before it is polled again. DLRs
    /// arrive minutes-to-hours later, so this is deliberately coarser than <see cref="PollInterval"/>.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long the provider retains queryable status for a message. Past this, an unresolved
    /// message is marked <c>Expired</c> and dequeued instead of being polled forever (Magfa keeps
    /// statuses ~24h).
    /// </summary>
    public TimeSpan StatusWindow { get; init; } = TimeSpan.FromHours(24);

    /// <summary>How many poll rows to claim (and provider message ids to query) per cycle.</summary>
    public int BatchSize { get; init; } = 100;
}
