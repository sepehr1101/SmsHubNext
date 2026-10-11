namespace SmsHubNext.Features.Authentication;

/// <summary>
/// Optional activation switch for a future API-key-only route group. Do not register it
/// globally: management routes use administrator JWTs and customer routes validate their
/// API keys at the endpoint boundary.
/// </summary>
public static class ApiKeyAuthenticationBuilderExtensions
{
    public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder app) =>
        app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
}
