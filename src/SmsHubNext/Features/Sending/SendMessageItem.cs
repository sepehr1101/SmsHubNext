namespace SmsHubNext.Features.Sending;

/// <summary>One recipient and the distinct message they should receive.</summary>
public sealed class SendMessageItem
{
    /// <summary>Recipient mobile number (ad-hoc).</summary>
    public string Recipient { get; init; } = string.Empty;

    /// <summary>The exact message text for this recipient (each item is independent).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Optional per-message idempotency key.</summary>
    public string? ClientCorrelatedId { get; init; }
}
