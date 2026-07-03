namespace SmsHubNext.Shared.Enums;

/// <summary>
/// Operational timeline event for a <c>MessageBatch</c>. Persisted as <c>TINYINT</c>;
/// values are stable and must not be renumbered.
/// </summary>
public enum MessageBatchEventType : byte
{
    Accepted = 1,
    DispatchStarted = 2,
    DispatchResumed = 3,
    Requeued = 4,
    AwaitingConfirmation = 5,
    Held = 6,
    MessageRejected = 7,
    Completed = 8,
    PartiallyFailed = 9,
    Failed = 10,
}
