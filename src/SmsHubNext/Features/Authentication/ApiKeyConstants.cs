namespace SmsHubNext.Features.Authentication;

/// <summary>Well-known names for API-key authentication.</summary>
public static class ApiKeyConstants
{
    /// <summary>The request header carrying the plaintext key (e.g. <c>shn_…</c>).</summary>
    public const string HeaderName = "X-Api-Key";

    /// <summary>
    /// Key under which the resolved <see cref="ApiKeyIdentity"/> is stashed on
    /// <c>HttpContext.Items</c> when enforcement is active.
    /// </summary>
    public const string HttpContextItemKey = "SmsHubNext.ApiKeyIdentity";
}
