using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

[ApiController]
[Route("api-keys/{apiKeyId:int}/ip-restrictions")]
public sealed class ApiKeyIpRestrictionsController : BaseController
{
    private readonly AddIpRestrictionHandler _add;
    private readonly ListIpRestrictionsHandler _list;

    public ApiKeyIpRestrictionsController(AddIpRestrictionHandler add, ListIpRestrictionsHandler list)
    {
        _add = add;
        _list = list;
    }

    /// <summary>List an API key's allowed CIDR ranges.</summary>
    [HttpGet]
    public async Task<IActionResult> List(int apiKeyId, CancellationToken cancellationToken) =>
        FromResult(await _list.Handle(apiKeyId, cancellationToken));

    /// <summary>Add an allowed CIDR range to an API key.</summary>
    [HttpPost]
    public async Task<IActionResult> Add(
        int apiKeyId,
        [FromBody] AddIpRestrictionRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _add.Handle(apiKeyId, request, cancellationToken), StatusCodes.Status201Created);
}
