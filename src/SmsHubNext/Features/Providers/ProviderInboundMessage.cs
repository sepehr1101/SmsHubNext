namespace SmsHubNext.Features.Providers;

/// <summary>
/// One inbound (MO) message fetched from a provider's inbox: who sent it, which of our numbers
/// received it, the text, and the provider's reported timestamp (verbatim — its time zone is not
/// specified, so it is kept as a string rather than coerced to UTC).
/// </summary>
public sealed record ProviderInboundMessage(
    string SenderNumber, string RecipientNumber, string Body, string? ProviderTimestamp);
