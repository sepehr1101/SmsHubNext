using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Sending;

[ApiController]
[Route("messages")]
public sealed class ResendMessageController(ApiKeyAuthenticator authenticator, ResendMessageHandler handler) : BaseController
{
    /// <summary>Queue a new, separately billed copy of one of the calling customer's completed messages.</summary>
    /// <remarks>Requires X-Api-Key and a nonempty requestId UUID. Repeat the UUID to safely retry a lost response.</remarks>
    [HttpPost("{messageId:long}/resend")]
    [AllowAnonymous] // Customer API key is checked explicitly; cookies/JWTs do not authorize this route.
    public async Task<IActionResult> Resend(long messageId, [FromBody] ResendMessageRequest request, CancellationToken token)
    {
        Result<ApiKeyIdentity> identity = await authenticator.Authenticate(
            Request.Headers[ApiKeyConstants.HeaderName].ToString(), HttpContext.Connection.RemoteIpAddress, token);
        if (identity.IsFailure) return FromResult(identity);
        if (identity.Value.AccessLevel != ApiKeyAccessLevel.Customer)
            return FromError(Error.Forbidden("auth.customer_key_required", UserMessages.Auth.CustomerKeyRequired));
        return FromResult(await handler.Handle(messageId, request, identity.Value, token), StatusCodes.Status202Accepted);
    }
}
