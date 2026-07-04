using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Providers;

[ApiController]
[Route("reference-data/providers")]
public sealed class ProvidersController : BaseController
{
    private readonly ListProvidersHandler _list;
    private readonly CreateProviderHandler _create;

    public ProvidersController(ListProvidersHandler list, CreateProviderHandler create)
    {
        _list = list;
        _create = create;
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
}
