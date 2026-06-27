namespace SmsHubNext.Features.ApiKeys;

/// <summary>An allowed source CIDR for an API key (README §4.3).</summary>
public sealed record ApiKeyIpRestriction(int Id, int ApiKeyId, string Cidr, string? Description);
