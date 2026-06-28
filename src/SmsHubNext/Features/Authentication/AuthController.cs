using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Authentication;

[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly ApiKeyAuthenticator _authenticator;

    public AuthController(ApiKeyAuthenticator authenticator) => _authenticator = authenticator;

    /// <summary>
    /// Resolve the <c>X-Api-Key</c> header and echo the caller it maps to — a convenience
    /// for testing keys without enforcing auth on the other endpoints. Returns 200 with the
    /// identity for a valid key, or 401 otherwise.
    /// </summary>
    [HttpGet("whoami")]
    public async Task<IActionResult> WhoAmI(CancellationToken cancellationToken)
    {
        var rawKey = Request.Headers[ApiKeyConstants.HeaderName].ToString();

        var result = await _authenticator.Authenticate(
            rawKey,
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        return result.ToActionResult();
    }
}
