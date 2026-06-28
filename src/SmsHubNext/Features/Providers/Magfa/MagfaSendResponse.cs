using System.Text.Json.Serialization;

namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// The JSON body of a <c>POST /api/http/sms/v2/send</c> response (API reference §5). The
/// top-level <see cref="Status"/> is request-level; <see cref="Messages"/> holds one
/// per-recipient outcome. We send one message per call, so we read <c>Messages[0]</c>.
/// </summary>
public sealed class MagfaSendResponse
{
    /// <summary>Request-level status: <c>0</c> = accepted, else an error code (reference §8).</summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("messages")]
    public IReadOnlyList<MagfaSentMessage> Messages { get; init; } = [];
}

/// <summary>One per-recipient result inside a <see cref="MagfaSendResponse"/>.</summary>
public sealed class MagfaSentMessage
{
    /// <summary>Per-message status: <c>0</c> = accepted, else an error code (reference §8).</summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }

    /// <summary>Provider message id (the DLR-matching key); present only when <see cref="Status"/> is 0.</summary>
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    /// <summary>Echo of the <c>uid</c> we supplied, if any.</summary>
    [JsonPropertyName("userId")]
    public long? UserId { get; init; }

    [JsonPropertyName("parts")]
    public int? Parts { get; init; }

    [JsonPropertyName("tariff")]
    public decimal? Tariff { get; init; }

    /// <summary><c>DEFAULT</c> (GSM-7) or <c>UCS2</c> (Persian).</summary>
    [JsonPropertyName("alphabet")]
    public string? Alphabet { get; init; }

    [JsonPropertyName("recipient")]
    public string? Recipient { get; init; }
}
