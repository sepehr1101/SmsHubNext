namespace SmsHubNext.Features.Providers.SmsIr;

public sealed class SmsIrAccount
{
    public required string ApiKey { get; init; }

    public IReadOnlyList<string> SenderLines { get; init; } = [];
}
