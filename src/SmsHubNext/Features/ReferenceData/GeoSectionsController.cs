using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

[ApiController]
[Route("reference-data/geo-sections")]
public sealed class GeoSectionsController : ControllerBase
{
    private readonly ListGeoSectionsHandler _handler;

    public GeoSectionsController(ListGeoSectionsHandler handler) => _handler = handler;

    /// <summary>List the geographic sections.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await _handler.Handle(cancellationToken)).ToActionResult();
}
