namespace SmsHubNext.Features.Providers;

/// <summary>
/// One message to hand to a provider: our <see cref="MessageId"/> (carried as the provider's
/// correlation id / <c>uid</c> where supported, enabling idempotent resend after a transport
/// timeout), the originating line, the recipient, and the body.
/// </summary>
public sealed record ProviderSendRequest(long MessageId, string SenderLine, string MobileNumber, string Body);
