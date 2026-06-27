using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

[ApiController]
[Route("reference-data/geo-sections")]
public sealed class GeoSectionsController : ControllerBase
{
    private readonly ListGeoSectionsHandler _list;
    private readonly CreateGeoSectionHandler _create;

    public GeoSectionsController(ListGeoSectionsHandler list, CreateGeoSectionHandler create)
    {
        _list = list;
        _create = create;
    }

    /// <summary>List the geographic sections.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await _list.Handle(cancellationToken)).ToActionResult();

    /// <summary>Create a geographic section under an optional parent.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateGeoSectionRequest request,
        CancellationToken cancellationToken) =>
        (await _create.Handle(request, cancellationToken)).ToActionResult(StatusCodes.Status201Created);
}
