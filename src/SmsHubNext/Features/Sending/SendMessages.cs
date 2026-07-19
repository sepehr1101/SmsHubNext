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
    public const int MaxClientBatchIdLength = 100;
    public const int MaxTextLength = 1800;
    public const int MaxClientCorrelatedIdLength = 100;
    public const int MaxBillIdLength = 31;
    public const int MaxPayIdLength = 31;

    /// <summary>
    /// Deprecated input kept temporarily for compatibility; send attribution uses the authenticated
    /// API key's customer, never this body value.
    /// </summary>
    public short CustomerId { get; init; }

    /// <summary>The originating sender line for the whole batch (e.g. <c>3000...</c>).</summary>
    public string SenderLine { get; init; } = string.Empty;

    /// <summary>The classification for every message in the batch (delivery class + business purpose).</summary>
    public byte MessageTypeId { get; init; }

    /// <summary>Required batch-level idempotency key, unique per customer and logical request.</summary>
    public string ClientBatchId { get; init; } = string.Empty;

    /// <summary>The recipient/message pairs to send.</summary>
    public IReadOnlyList<SendMessageItem> Messages { get; init; } = [];

    /// <summary>
    /// Plain, in-feature validation (ARCHITECTURE.md §6). Validates the request and
    /// every item; returns the first problem found (with its item index).
    /// </summary>
    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(SenderLine))
            return Error.Validation("sending.sender_line_required", UserMessages.Sending.SenderLineRequired);

        if (MessageTypeId == 0)
            return Error.Validation("sending.message_type_required", UserMessages.Sending.MessageTypeRequired);

        if (string.IsNullOrWhiteSpace(ClientBatchId))
            return Error.Validation("sending.client_batch_id_required", UserMessages.Sending.ClientBatchIdRequired);

        if (ClientBatchId.Length > MaxClientBatchIdLength)
            return Error.Validation("sending.client_batch_id_too_long", UserMessages.Sending.ClientBatchIdTooLong);

        if (Messages.Count == 0)
            return Error.Validation("sending.messages_required", UserMessages.Sending.MessagesRequired);

        if (Messages.Count > MaxMessages)
            return Error.Validation(
                "sending.too_many_messages",
                UserMessages.Sending.TooManyMessagesLimit(MaxMessages));

        for (int index = 0; index < Messages.Count; index++)
        {
            SendMessageItem message = Messages[index];

            if (string.IsNullOrWhiteSpace(message.Recipient))
                return Error.Validation(
                    "sending.recipient_required",
                    UserMessages.Sending.MissingRecipient(index));

            if (!SendMessageItem.IsValidRecipient(message.Recipient))
                return Error.Validation(
                    "sending.recipient_invalid",
                    UserMessages.Sending.InvalidRecipient(index));

            if (string.IsNullOrWhiteSpace(message.Text))
                return Error.Validation(
                    "sending.text_required",
                    UserMessages.Sending.MissingText(index));

            if (message.Text.Length > MaxTextLength)
                return Error.Validation(
                    "sending.text_too_long",
                    UserMessages.Sending.TextTooLong(index, MaxTextLength));

            if (message.ClientCorrelatedId?.Length > MaxClientCorrelatedIdLength)
                return Error.Validation(
                    "sending.client_correlated_id_too_long",
                    UserMessages.Sending.ClientCorrelatedIdTooLongAt(index, MaxClientCorrelatedIdLength));

            if (message.BillId?.Length > MaxBillIdLength)
                return Error.Validation(
                    "sending.bill_id_too_long",
                    UserMessages.Sending.BillIdTooLong(index, MaxBillIdLength));

            if (message.PayId?.Length > MaxPayIdLength)
                return Error.Validation(
                    "sending.pay_id_too_long",
                    UserMessages.Sending.PayIdTooLong(index, MaxPayIdLength));
        }

        return Result.Success();
    }
}

/// <summary>
/// Acknowledgement that a send request was accepted and persisted. The batch is
/// processed asynchronously; callers poll its status later (accept → dispatch → status).
/// </summary>
public sealed record SendMessagesResponse(long BatchId, int AcceptedCount, bool IsDuplicate = false);
