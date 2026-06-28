namespace SmsHubNext.Features.Authentication;

/// <summary>
/// The activation switch for API-key enforcement. Calling this in the pipeline turns
/// auth ON for every endpoint downstream; it is deliberately NOT called yet (the APIs
/// stay open for testing). When ready, add <c>app.UseApiKeyAuthentication();</c> in
/// <c>ApplicationBuilderExtensions.ConfigurePipeline</c> just before <c>MapControllers</c>.
/// </summary>
public static class ApiKeyAuthenticationBuilderExtensions
{
    public static IApplicationBuilder UseApiKeyAuthentication(this IApplicationBuilder app) =>
        app.UseMiddleware<ApiKeyAuthenticationMiddleware>();
}
