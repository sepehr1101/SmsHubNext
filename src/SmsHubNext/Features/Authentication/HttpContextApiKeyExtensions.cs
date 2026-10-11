namespace SmsHubNext.Features.Authentication;

/// <summary>Reads the authenticated <see cref="ApiKeyIdentity"/> off the request.</summary>
public static class HttpContextApiKeyExtensions
{
    /// <summary>
    /// The identity resolved by <see cref="ApiKeyAuthenticationMiddleware"/>, or
    /// <c>null</c> when enforcement is inactive or the request was not authenticated.
    /// </summary>
    public static ApiKeyIdentity? GetApiKeyIdentity(this HttpContext context)
    {
        if (context.Items.TryGetValue(ApiKeyConstants.HttpContextItemKey, out object? value) &&
            value is ApiKeyIdentity resolvedIdentity)
        {
            return resolvedIdentity;
        }

        if (context.User.Identity?.IsAuthenticated != true)
            return null;

        string? apiKeyIdValue = context.User.FindFirst(AuthenticationClaimTypes.ApiKeyId)?.Value;
        string? customerIdValue = context.User.FindFirst(AuthenticationClaimTypes.CustomerId)?.Value;
        string? keyPrefix = context.User.FindFirst(AuthenticationClaimTypes.KeyPrefix)?.Value;
        string? accessLevelValue = context.User.FindFirst(AuthenticationClaimTypes.AccessLevel)?.Value;

        if (!int.TryParse(apiKeyIdValue, out int apiKeyId) ||
            !short.TryParse(customerIdValue, out short customerId) ||
            string.IsNullOrWhiteSpace(keyPrefix) ||
            !byte.TryParse(accessLevelValue, out byte accessLevel) ||
            !Enum.IsDefined((ApiKeyAccessLevel)accessLevel))
        {
            return null;
        }

        return new ApiKeyIdentity(
            apiKeyId,
            customerId,
            keyPrefix,
            (ApiKeyAccessLevel)accessLevel);
    }
}
