using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

[ApiController]
[Route("reference-data/providers")]
public sealed class ProvidersController : ControllerBase
{
    private readonly ListProvidersHandler _handler;

    public ProvidersController(ListProvidersHandler handler) => _handler = handler;

    /// <summary>List the SMS providers.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await _handler.Handle(cancellationToken)).ToActionResult();
}
