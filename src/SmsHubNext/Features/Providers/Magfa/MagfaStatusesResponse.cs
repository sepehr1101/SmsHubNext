using System.Text.Json.Serialization;

namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// The JSON body of a <c>GET /api/http/sms/v2/statuses/{mids}</c> response (API reference §7):
/// a request-level <see cref="Status"/> and one <see cref="MagfaDlr"/> per queried mid.
/// </summary>
public sealed class MagfaStatusesResponse
{
    /// <summary>Request-level status: <c>0</c> = ok, else an error code (reference §8).</summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("dlrs")]
    public IReadOnlyList<MagfaDlr> Dlrs { get; init; } = [];
}

/// <summary>One delivery-status entry for a single message id.</summary>
public sealed class MagfaDlr
{
    /// <summary>Provider message id (matches <c>Message.ProviderMessageId</c>).</summary>
    [JsonPropertyName("mid")]
    public long Mid { get; init; }

    /// <summary>Magfa DLR code (reference §7): 1 delivered, 2/16 failed, 8/0 in-flight, -1 gone.</summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }

    /// <summary>Provider timestamp, <c>yyyy-MM-dd HH:mm:ss</c> (informational; no UTC offset given).</summary>
    [JsonPropertyName("date")]
    public string? Date { get; init; }
}
