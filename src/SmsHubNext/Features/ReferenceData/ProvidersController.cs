using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

[ApiController]
[Route("reference-data/providers")]
public sealed class ProvidersController : ControllerBase
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
        (await _list.Handle(cancellationToken)).ToActionResult();

    /// <summary>Create an SMS provider.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProviderRequest request,
        CancellationToken cancellationToken) =>
        (await _create.Handle(request, cancellationToken)).ToActionResult(StatusCodes.Status201Created);
}
