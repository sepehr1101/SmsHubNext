namespace SmsHubNext.App.Features.Inbound;

public sealed record InboundMessageItem(long Id, byte ProviderId, string SenderNumber, string RecipientNumber, string Body, string? ProviderTimestamp, DateTime ReceivedAtUtc);
