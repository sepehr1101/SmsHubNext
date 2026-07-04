using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

[ApiController]
[Route("api-keys")]
public sealed class ApiKeysController : BaseController
{
    private readonly IssueApiKeyHandler _issue;
    private readonly ListApiKeysHandler _list;

    public ApiKeysController(IssueApiKeyHandler issue, ListApiKeysHandler list)
    {
        _issue = issue;
        _list = list;
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
}
