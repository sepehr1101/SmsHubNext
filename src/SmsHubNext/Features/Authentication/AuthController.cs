using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Authentication;

[ApiController]
[Route("auth")]
public sealed class AuthController : BaseController
{
    private readonly ApiKeyAuthenticator _authenticator;
    private readonly ExchangeAdminTokenHandler _exchangeAdminToken;

    public AuthController(
        ApiKeyAuthenticator authenticator,
        ExchangeAdminTokenHandler exchangeAdminToken)
    {
        _authenticator = authenticator;
        _exchangeAdminToken = exchangeAdminToken;
    }

    /// <summary>Exchange an administrator API key for a short-lived management JWT.</summary>
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationPolicies.TokenExchangeRateLimit)]
    [HttpPost("token")]
    public async Task<IActionResult> Token(CancellationToken cancellationToken)
    {
        Result<AdminAccessTokenResponse> result = await _exchangeAdminToken.Handle(
            Request.Headers[ApiKeyConstants.HeaderName].ToString(),
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        return FromResult(result);
    }

    /// <summary>
    /// Resolve the <c>X-Api-Key</c> header and echo the caller it maps to — a convenience
    /// for testing keys without enforcing auth on the other endpoints. Returns 200 with the
    /// identity for a valid key, or 401 otherwise.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("whoami")]
    public async Task<IActionResult> WhoAmI(CancellationToken cancellationToken)
    {
        string rawKey = Request.Headers[ApiKeyConstants.HeaderName].ToString();

        Result<ApiKeyIdentity> result = await _authenticator.Authenticate(
            rawKey,
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        return FromResult(result);
    }
}
