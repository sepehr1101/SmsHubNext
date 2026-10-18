namespace SmsHubNext.Features.Authentication;

/// <summary>
/// The authority granted to an API key. Customer keys can submit tenant work;
/// administrator keys can exchange their secret for a short-lived management JWT.
/// </summary>
public enum ApiKeyAccessLevel : byte
{
    Customer = 0,
    Administrator = 1,
}
