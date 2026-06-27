using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Sending;

/// <summary>One send request: a batch of recipients sharing a single message body.</summary>
public sealed class SendMessagesRequest
{
    /// <summary>The originating sender line (e.g. <c>3000...</c>).</summary>
    public string SenderLine { get; init; } = string.Empty;

    /// <summary>Ad-hoc recipient mobile numbers.</summary>
    public IReadOnlyList<string> Recipients { get; init; } = [];

    /// <summary>The exact message text (caller-supplied; each message is distinct).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Optional caller idempotency key.</summary>
    public string? ClientCorrelatedId { get; init; }

    /// <summary>
    /// Plain, in-feature validation (ARCHITECTURE.md §6). Returns the first problem
    /// found, or <see cref="Result.Success()"/> when the request is well-formed.
    /// </summary>
    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(SenderLine))
            return Error.Validation("sending.sender_line_required", "A sender line is required.");

        if (Recipients.Count == 0)
            return Error.Validation("sending.recipients_required", "At least one recipient is required.");

        if (string.IsNullOrWhiteSpace(Text))
            return Error.Validation("sending.text_required", "Message text is required.");

        return Result.Success();
    }
}
