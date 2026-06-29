using System.Text.Json.Serialization;

namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// The JSON body of a <c>GET /api/http/sms/v2/messages/{count}</c> response (API reference §9):
/// a request-level <see cref="Status"/> and the pulled inbound messages. The pull is destructive —
/// returned messages are dequeued at Magfa.
/// </summary>
public sealed class MagfaMessagesResponse
{
    /// <summary>Request-level status: <c>0</c> = ok, else an error code (reference §8).</summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("messages")]
    public IReadOnlyList<MagfaInboundMessage> Messages { get; init; } = [];
}

/// <summary>One inbound (MO) message in a <see cref="MagfaMessagesResponse"/>.</summary>
public sealed class MagfaInboundMessage
{
    [JsonPropertyName("body")]
    public string Body { get; init; } = string.Empty;

    /// <summary>The external mobile that sent the message.</summary>
    [JsonPropertyName("senderNumber")]
    public string SenderNumber { get; init; } = string.Empty;

    /// <summary>Our number that received it.</summary>
    [JsonPropertyName("recipientNumber")]
    public string RecipientNumber { get; init; } = string.Empty;

    /// <summary>Provider timestamp, <c>yyyy-MM-dd HH:mm:ss</c> (informational; no UTC offset given).</summary>
    [JsonPropertyName("date")]
    public string? Date { get; init; }
}
