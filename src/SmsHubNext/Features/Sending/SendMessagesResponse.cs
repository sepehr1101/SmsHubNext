namespace SmsHubNext.Features.Sending;

/// <summary>
/// Acknowledgement that a send request was accepted and persisted. The batch is
/// processed asynchronously; callers poll its status later (accept → dispatch → status).
/// </summary>
public sealed record SendMessagesResponse(long BatchId, int AcceptedCount);
