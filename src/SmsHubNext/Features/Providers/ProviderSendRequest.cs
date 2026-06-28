namespace SmsHubNext.Features.Providers;

/// <summary>One message to hand to a provider: the originating line, the recipient, and the body.</summary>
public sealed record ProviderSendRequest(string SenderLine, string MobileNumber, string Body);
