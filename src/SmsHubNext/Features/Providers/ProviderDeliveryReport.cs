using SmsHubNext.Shared.Enums;

namespace SmsHubNext.Features.Providers;

/// <summary>
/// One delivery-status result from a provider's DLR query, keyed by the provider's own
/// message id. The provider normalizes its native code to our <see cref="DeliveryReportStatus"/>
/// vocabulary (it owns that mapping, just as it owns send-result mapping):
/// <list type="bullet">
/// <item><see cref="Status"/> is <c>null</c> while the message is still in flight (no terminal
/// outcome yet) — the poller keeps it queued and re-polls later.</item>
/// <item>a non-null <see cref="Status"/> is a terminal outcome — the poller projects it onto
/// <c>Message.DeliveryStatus</c>, appends a <c>DeliveryReport</c>, and dequeues the message.</item>
/// </list>
/// <see cref="RawStatusCode"/> is the provider-native code, kept verbatim for forensics.
/// </summary>
public sealed record ProviderDeliveryReport(string ProviderMessageId, DeliveryReportStatus? Status, int RawStatusCode);
