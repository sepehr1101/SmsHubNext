using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Providers;

[ApiController]
[Route("reference-data/providers")]
public sealed class ProvidersController : BaseController
{
    private readonly ListProvidersHandler _list;
    private readonly CreateProviderHandler _create;
    private readonly UpdateProviderHandler _update;
    private readonly DeleteProviderHandler _delete;

    public ProvidersController(
        ListProvidersHandler list,
        CreateProviderHandler create,
        UpdateProviderHandler update,
        DeleteProviderHandler delete)
    {
        _list = list;
        _create = create;
        _update = update;
        _delete = delete;
    }

    /// <summary>List the SMS providers.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        FromResult(await _list.Handle(cancellationToken));

    /// <summary>Create an SMS provider.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProviderRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _create.Handle(request, cancellationToken), StatusCodes.Status201Created);

    /// <summary>Update provider configuration; the stable provider code is immutable.</summary>
    [HttpPut("{id:int:range(1,255)}")]
    public async Task<IActionResult> Update(
        byte id,
        [FromBody] UpdateProviderRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _update.Handle(id, request, cancellationToken));

    /// <summary>Soft-delete a provider while retaining historical references.</summary>
    [HttpDelete("{id:int:range(1,255)}")]
    public async Task<IActionResult> Delete(byte id, CancellationToken cancellationToken) =>
        FromResult(await _delete.Handle(
            id,
            HttpContext.GetApiKeyIdentity()!.ApiKeyId,
            cancellationToken));
}
