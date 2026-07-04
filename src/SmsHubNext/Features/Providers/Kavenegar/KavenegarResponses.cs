using System.Text.Json.Serialization;

namespace SmsHubNext.Features.Providers.Kavenegar;

public sealed class KavenegarResponse<T>
{
    [JsonPropertyName("return")]
    public KavenegarReturn Return { get; init; } = new();

    [JsonPropertyName("entries")]
    public T? Entries { get; init; }
}

public sealed class KavenegarReturn
{
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;
}

public sealed class KavenegarMessageEntry
{
    [JsonPropertyName("messageid")]
    public long? MessageId { get; init; }

    [JsonPropertyName("localid")]
    public string? LocalId { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("statustext")]
    public string? StatusText { get; init; }

    [JsonPropertyName("sender")]
    public string? Sender { get; init; }

    [JsonPropertyName("receptor")]
    public string? Receptor { get; init; }

    [JsonPropertyName("date")]
    public long? Date { get; init; }

    [JsonPropertyName("cost")]
    public int? Cost { get; init; }
}

public sealed class KavenegarInboundEntry
{
    [JsonPropertyName("messageid")]
    public long MessageId { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("sender")]
    public string Sender { get; init; } = string.Empty;

    [JsonPropertyName("receptor")]
    public string Receptor { get; init; } = string.Empty;

    [JsonPropertyName("date")]
    public long Date { get; init; }
}
