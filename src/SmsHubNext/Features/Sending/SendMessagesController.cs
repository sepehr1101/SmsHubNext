using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Sending;

[ApiController]
[Route("messages")]
public sealed class SendMessagesController : ControllerBase
{
    private readonly SendMessagesHandler _handler;
    private readonly ApiKeyAuthenticator _authenticator;

    public SendMessagesController(SendMessagesHandler handler, ApiKeyAuthenticator authenticator)
    {
        _handler = handler;
        _authenticator = authenticator;
    }

    /// <summary>Accept a batch of messages for asynchronous sending.</summary>
    /// <remarks>
    /// The calling API key is resolved from the request, never the body: the identity stashed by
    /// the auth middleware when enforcement is active, otherwise the <c>X-Api-Key</c> header
    /// (resolved here so attribution works even while the middleware stays off for testing).
    /// </remarks>
    [HttpPost]
    public async Task<IActionResult> Send(
        [FromBody] SendMessagesRequest request,
        CancellationToken cancellationToken)
    {
        Result<int> apiKeyId = await ResolveApiKeyId(cancellationToken);
        if (apiKeyId.IsFailure)
            return apiKeyId.ToActionResult();

        return (await _handler.Handle(request, apiKeyId.Value, cancellationToken))
            .ToActionResult(StatusCodes.Status202Accepted);
    }

    private async Task<Result<int>> ResolveApiKeyId(CancellationToken cancellationToken)
    {
        // Prefer the identity the auth middleware already resolved (enforcement active).
        ApiKeyIdentity? identity = HttpContext.GetApiKeyIdentity();
        if (identity is not null)
            return identity.ApiKeyId;

        // Middleware off: resolve the X-Api-Key header here so the key is still never taken from the body.
        Result<ApiKeyIdentity> resolved = await _authenticator.Authenticate(
            Request.Headers[ApiKeyConstants.HeaderName].ToString(),
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);

        return resolved.IsFailure ? resolved.Error! : resolved.Value.ApiKeyId;
    }
}
