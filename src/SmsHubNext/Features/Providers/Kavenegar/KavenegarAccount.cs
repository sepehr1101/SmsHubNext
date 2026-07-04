namespace SmsHubNext.Features.Providers.Kavenegar;

/// <summary>
/// One Kavenegar API key and the sender/inbound lines it owns.
/// </summary>
public sealed class KavenegarAccount
{
    public string ApiKey { get; init; } = string.Empty;

    public IReadOnlyList<string> SenderLines { get; init; } = [];

    public IReadOnlyList<string> InboundLines { get; init; } = [];
}
