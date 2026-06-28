namespace SmsHubNext.Features.Authentication;

/// <summary>Reads the authenticated <see cref="ApiKeyIdentity"/> off the request.</summary>
public static class HttpContextApiKeyExtensions
{
    /// <summary>
    /// The identity resolved by <see cref="ApiKeyAuthenticationMiddleware"/>, or
    /// <c>null</c> when enforcement is inactive or the request was not authenticated.
    /// </summary>
    public static ApiKeyIdentity? GetApiKeyIdentity(this HttpContext context) =>
        context.Items.TryGetValue(ApiKeyConstants.HttpContextItemKey, out object? value)
            ? value as ApiKeyIdentity
            : null;
}
