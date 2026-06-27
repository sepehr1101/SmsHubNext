namespace SmsHubNext.Shared.Enums;

/// <summary>
/// Authoritative current state of a <c>MessageBatch</c> (one API call, README §4.13).
/// Persisted as <c>TINYINT</c> — values are stable and must not be renumbered.
/// </summary>
public enum BatchStatus : byte
{
    Received = 1,
    Dispatching = 2,
    Completed = 3,
    PartiallyFailed = 4,
    Held = 5,
    Rejected = 6,
    Failed = 7,
}

public static class BatchStatusExtensions
{
    /// <summary>
    /// True for the end states that set <c>FinishedAtUtc</c>. Received, Dispatching
    /// and Held are non-terminal — a <c>Held</c> batch keeps its messages <c>Queued</c>
    /// and is resumed by a worker rather than being burned as failed (README §4.13).
    /// </summary>
    public static bool IsTerminal(this BatchStatus status) => status switch
    {
        BatchStatus.Completed or BatchStatus.PartiallyFailed
            or BatchStatus.Rejected or BatchStatus.Failed => true,
        _ => false,
    };
}
