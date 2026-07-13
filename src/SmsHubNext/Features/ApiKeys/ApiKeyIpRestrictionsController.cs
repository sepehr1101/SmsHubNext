using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ApiKeys;

[ApiController]
[Route("api-keys/{apiKeyId:int}/ip-restrictions")]
public sealed class ApiKeyIpRestrictionsController : BaseController
{
    private readonly AddIpRestrictionHandler _add;
    private readonly ListIpRestrictionsHandler _list;
    private readonly UpdateIpRestrictionHandler _update;
    private readonly DeleteIpRestrictionHandler _delete;

    public ApiKeyIpRestrictionsController(
        AddIpRestrictionHandler add,
        ListIpRestrictionsHandler list,
        UpdateIpRestrictionHandler update,
        DeleteIpRestrictionHandler delete)
    {
        _add = add;
        _list = list;
        _update = update;
        _delete = delete;
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

    /// <summary>Update a live CIDR restriction.</summary>
    [HttpPut("{id:int:min(1)}")]
    public async Task<IActionResult> Update(
        int apiKeyId,
        int id,
        [FromBody] UpdateIpRestrictionRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _update.Handle(apiKeyId, id, request, cancellationToken));

    /// <summary>Soft-delete a CIDR restriction.</summary>
    [HttpDelete("{id:int:min(1)}")]
    public async Task<IActionResult> Delete(
        int apiKeyId,
        int id,
        CancellationToken cancellationToken) =>
        FromResult(await _delete.Handle(
            apiKeyId,
            id,
            HttpContext.GetApiKeyIdentity()!.ApiKeyId,
            cancellationToken));
}
