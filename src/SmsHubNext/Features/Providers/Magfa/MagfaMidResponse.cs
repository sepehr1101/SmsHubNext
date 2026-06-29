using System.Text.Json.Serialization;

namespace SmsHubNext.Features.Providers.Magfa;

/// <summary>
/// The JSON body of a <c>GET /api/http/sms/v2/mid/{uid}</c> response (API reference §6): the
/// provider message id Magfa holds for a uid we previously sent, or <c>-1</c> if it has no record.
/// </summary>
public sealed class MagfaMidResponse
{
    /// <summary>Request-level status: <c>0</c> = ok, else an error code (reference §8).</summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }

    /// <summary>The accepted message's provider id, or <c>-1</c> when the uid is unknown.</summary>
    [JsonPropertyName("mid")]
    public long Mid { get; init; }
}
