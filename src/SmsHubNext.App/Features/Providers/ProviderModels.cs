namespace SmsHubNext.App.Features.Providers;

public sealed record ProviderItem(byte Id, string Name, string Code, bool IsActive);
public sealed record CreateProviderRequest(string Name, string Code, string BaseUrl, string? FallbackBaseUrl);
public sealed record CreateProviderResponse(byte Id);
public sealed record UpdateProviderRequest(string Name, string BaseUrl, string? FallbackBaseUrl, bool IsActive);
