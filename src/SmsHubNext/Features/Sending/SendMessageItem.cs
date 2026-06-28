namespace SmsHubNext.Features.Sending;

/// <summary>One recipient and the distinct message they should receive.</summary>
public sealed class SendMessageItem
{
    /// <summary>Recipient mobile number (ad-hoc).</summary>
    public string Recipient { get; init; } = string.Empty;

    /// <summary>The exact message text for this recipient (each item is independent).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Optional per-message idempotency key (maps to the provider's <c>uid</c>).</summary>
    public string? ClientCorrelatedId { get; init; }

    /// <summary>Optional external bill reference for reconciliation.</summary>
    public string? BillId { get; init; }

    /// <summary>Optional external payment reference for reconciliation.</summary>
    public string? PayId { get; init; }

    /// <summary>Optional caller-supplied geographic section (province/city/zone) for reporting.</summary>
    public int? GeoSectionId { get; init; }
}
