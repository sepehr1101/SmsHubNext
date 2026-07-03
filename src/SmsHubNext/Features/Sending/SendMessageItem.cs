namespace SmsHubNext.Features.Sending;

/// <summary>One recipient and the distinct message they should receive.</summary>
public sealed class SendMessageItem
{
    public const int LocalIranMobileLength = 11;
    public const int InternationalIranMobileLength = 12;

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

    public static bool IsValidRecipient(string recipient)
    {
        if (string.IsNullOrWhiteSpace(recipient))
            return false;

        string trimmed = recipient.Trim();
        foreach (char character in trimmed)
        {
            if (!char.IsAsciiDigit(character))
                return false;
        }

        return (trimmed.Length == LocalIranMobileLength && trimmed.StartsWith("09", StringComparison.Ordinal))
            || (trimmed.Length == InternationalIranMobileLength && trimmed.StartsWith("989", StringComparison.Ordinal));
    }
}
