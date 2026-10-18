using System.Text.Json.Serialization;

namespace SmsHubNext.Features.Providers.SmsIr;

public sealed class SmsIrResponse<T>
{
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public T? Data { get; init; }
}

public sealed class SmsIrLikeToLikeRequest
{
    [JsonPropertyName("lineNumber")]
    public long LineNumber { get; init; }

    [JsonPropertyName("messageTexts")]
    public IReadOnlyList<string> MessageTexts { get; init; } = [];

    [JsonPropertyName("mobiles")]
    public IReadOnlyList<string> Mobiles { get; init; } = [];

    [JsonPropertyName("sendDateTime")]
    public long? SendDateTime { get; init; }
}

public sealed class SmsIrSendData
{
    [JsonPropertyName("packId")]
    public Guid PackId { get; init; }

    [JsonPropertyName("messageIds")]
    public IReadOnlyList<long?> MessageIds { get; init; } = [];

    [JsonPropertyName("cost")]
    public decimal Cost { get; init; }
}

public sealed class SmsIrMessageData
{
    [JsonPropertyName("messageId")]
    public long MessageId { get; init; }

    [JsonPropertyName("deliveryState")]
    public byte? DeliveryState { get; init; }
}

public sealed class SmsIrInboundData
{
    [JsonPropertyName("receiveReturnId")]
    public long ReceiveReturnId { get; init; }

    [JsonPropertyName("messageText")]
    public string MessageText { get; init; } = string.Empty;

    [JsonPropertyName("number")]
    public long Number { get; init; }

    [JsonPropertyName("mobile")]
    public long Mobile { get; init; }

    [JsonPropertyName("receivedDateTime")]
    public long ReceivedDateTime { get; init; }
}
