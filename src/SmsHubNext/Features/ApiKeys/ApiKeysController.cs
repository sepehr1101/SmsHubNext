using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

[ApiController]
[Route("api-keys")]
public sealed class ApiKeysController : BaseController
{
    private readonly IssueApiKeyHandler _issue;
    private readonly ListApiKeysHandler _list;
    private readonly UpdateApiKeyHandler _update;
    private readonly RevokeApiKeyHandler _revoke;

    public ApiKeysController(
        IssueApiKeyHandler issue,
        ListApiKeysHandler list,
        UpdateApiKeyHandler update,
        RevokeApiKeyHandler revoke)
    {
        _issue = issue;
        _list = list;
        _update = update;
        _revoke = revoke;
    }

    /// <summary>List a customer's API keys (never returns the secret).</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] short customerId, CancellationToken cancellationToken) =>
        FromResult(await _list.Handle(customerId, cancellationToken));

    /// <summary>Issue a new API key. The plaintext key is returned only in this response.</summary>
    [HttpPost]
    public async Task<IActionResult> Issue(
        [FromBody] IssueApiKeyRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _issue.Handle(request, cancellationToken), StatusCodes.Status201Created);

    /// <summary>Update a non-revoked API key's label, expiry, and active status.</summary>
    [HttpPut("{id:int:min(1)}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateApiKeyRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _update.Handle(id, request, cancellationToken));

    /// <summary>Revoke an API key permanently while retaining it for attribution.</summary>
    [HttpDelete("{id:int:min(1)}")]
    public async Task<IActionResult> Revoke(int id, CancellationToken cancellationToken) =>
        FromResult(await _revoke.Handle(
            id,
            HttpContext.GetApiKeyIdentity()!.ApiKeyId,
            cancellationToken));
}
