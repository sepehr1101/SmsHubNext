namespace SmsHubNext.Features.Inbound;

/// <summary>One received (MO) message: who sent it, which of our numbers received it, the text,
/// the provider's reported time (verbatim), and when we pulled it.</summary>
public sealed record InboundMessage(
    long Id,
    byte ProviderId,
    string SenderNumber,
    string RecipientNumber,
    string Body,
    string? ProviderTimestamp,
    DateTime ReceivedAtUtc);
