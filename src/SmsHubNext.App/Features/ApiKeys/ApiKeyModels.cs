namespace SmsHubNext.App.Features.ApiKeys;

public enum ApiKeyAccessLevel : byte { Customer = 0, Administrator = 1 }
public sealed record ApiKeyItem(int Id, short CustomerId, string Name, string KeyPrefix, ApiKeyAccessLevel AccessLevel, bool IsActive, DateTime? ExpiresAtUtc, DateTime? RevokedAtUtc, int? RevokedByApiKeyId, DateTime CreatedAtUtc);
public sealed record IssueApiKeyRequest(short CustomerId, string Name, DateTime? ExpiresAtUtc);
public sealed record IssueApiKeyResponse(int Id, string KeyPrefix, string Key);
public sealed record UpdateApiKeyRequest(string Name, DateTime? ExpiresAtUtc, bool IsActive);
public sealed record ApiKeyIpRestrictionItem(int Id, int ApiKeyId, string Cidr, string? Description);
public sealed record AddIpRestrictionRequest(string Cidr, string? Description);
public sealed record UpdateIpRestrictionRequest(string Cidr, string? Description);
