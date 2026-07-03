using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Sending;

/// <summary>
/// One send request: a batch of independent recipient/message pairs sent on a
/// single sender line, classified by one message type. Each item carries its own
/// text, so recipients can receive different messages (personalization, OTPs, invoices, …).
/// </summary>
public sealed class SendMessagesRequest
{
    /// <summary>Maximum messages accepted in a single request.</summary>
    public const int MaxMessages = 1000;

    /// <summary>
    /// Deprecated input kept temporarily for compatibility; send attribution uses the authenticated
    /// API key's customer, never this body value.
    /// </summary>
    public short CustomerId { get; init; }

    /// <summary>The originating sender line for the whole batch (e.g. <c>3000...</c>).</summary>
    public string SenderLine { get; init; } = string.Empty;

    /// <summary>The classification for every message in the batch (delivery class + business purpose).</summary>
    public byte MessageTypeId { get; init; }

    /// <summary>Optional batch-level idempotency key.</summary>
    public string? ClientBatchId { get; init; }

    /// <summary>The recipient/message pairs to send.</summary>
    public IReadOnlyList<SendMessageItem> Messages { get; init; } = [];

    /// <summary>
    /// Plain, in-feature validation (ARCHITECTURE.md §6). Validates the request and
    /// every item; returns the first problem found (with its item index).
    /// </summary>
    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(SenderLine))
            return Error.Validation("sending.sender_line_required", "A sender line is required.");

        if (MessageTypeId == 0)
            return Error.Validation("sending.message_type_required", "A message type id is required.");

        if (Messages.Count == 0)
            return Error.Validation("sending.messages_required", "At least one message is required.");

        if (Messages.Count > MaxMessages)
            return Error.Validation(
                "sending.too_many_messages",
                $"A request may contain at most {MaxMessages} messages.");

        for (int index = 0; index < Messages.Count; index++)
        {
            SendMessageItem message = Messages[index];

            if (string.IsNullOrWhiteSpace(message.Recipient))
                return Error.Validation(
                    "sending.recipient_required",
                    $"Message at index {index} is missing a recipient.");

            if (string.IsNullOrWhiteSpace(message.Text))
                return Error.Validation(
                    "sending.text_required",
                    $"Message at index {index} is missing text.");
        }

        return Result.Success();
    }
}

/// <summary>
/// Acknowledgement that a send request was accepted and persisted. The batch is
/// processed asynchronously; callers poll its status later (accept → dispatch → status).
/// </summary>
public sealed record SendMessagesResponse(long BatchId, int AcceptedCount);
