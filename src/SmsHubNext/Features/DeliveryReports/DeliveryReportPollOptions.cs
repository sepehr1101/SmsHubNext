namespace SmsHubNext.Features.DeliveryReports;

/// <summary>Tuning for the delivery-report poll worker (bound from the <c>DeliveryReportPolling</c> section).</summary>
public sealed class DeliveryReportPollOptions
{
    public const string SectionName = "DeliveryReportPolling";

    /// <summary>
    /// Whether the hosted polling worker runs. Keep enabled in production; a provider sandbox that
    /// deliberately stores no reports may disable it while still leaving the poll queue auditable.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>How long to idle when no poll row is due before checking again.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long after a poll a still-in-flight message waits before it is polled again. DLRs
    /// arrive minutes-to-hours later, so this is deliberately coarser than <see cref="PollInterval"/>.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long the provider retains queryable status for a message. Past this, an unresolved
    /// message is marked <c>Expired</c> only after a successful provider query, so outages do not
    /// become delivery outcomes (Magfa keeps statuses ~24h).
    /// </summary>
    public TimeSpan StatusWindow { get; init; } = TimeSpan.FromHours(24);

    /// <summary>How many poll rows to claim (and provider message ids to query) per cycle.</summary>
    public int BatchSize { get; init; } = 100;
}
