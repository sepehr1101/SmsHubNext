namespace SmsHubNext.Features.Authentication;

/// <summary>
/// The caller resolved from a valid API key: which key authenticated and the
/// customer (tenant) it belongs to. Send attribution uses this identity rather than
/// trusting tenant identifiers supplied in request bodies.
/// </summary>
public sealed record ApiKeyIdentity(int ApiKeyId, short CustomerId, string KeyPrefix);
