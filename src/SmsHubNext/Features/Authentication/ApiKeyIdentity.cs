namespace SmsHubNext.Features.Authentication;

/// <summary>
/// The caller resolved from a valid API key: which key authenticated and the
/// customer (tenant) it belongs to. This is what will replace the interim explicit
/// <c>CustomerId</c>/<c>ApiKeyId</c> request fields once enforcement is switched on.
/// </summary>
public sealed record ApiKeyIdentity(int ApiKeyId, short CustomerId, string KeyPrefix);
