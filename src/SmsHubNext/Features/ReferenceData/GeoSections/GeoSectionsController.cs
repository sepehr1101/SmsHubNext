using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.GeoSections;

[ApiController]
[Route("reference-data/geo-sections")]
public sealed class GeoSectionsController : BaseController
{
    private readonly ListGeoSectionsHandler _list;
    private readonly CreateGeoSectionHandler _create;
    private readonly UpdateGeoSectionHandler _update;
    private readonly DeleteGeoSectionHandler _delete;

    public GeoSectionsController(
        ListGeoSectionsHandler list,
        CreateGeoSectionHandler create,
        UpdateGeoSectionHandler update,
        DeleteGeoSectionHandler delete)
    {
        _list = list;
        _create = create;
        _update = update;
        _delete = delete;
    }

    /// <summary>List the geographic sections.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        FromResult(await _list.Handle(cancellationToken));

    /// <summary>Create a geographic section under an optional parent.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateGeoSectionRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _create.Handle(request, cancellationToken), StatusCodes.Status201Created);

    /// <summary>Update display data/status; hierarchy and materialized path remain immutable.</summary>
    [HttpPut("{id:int:min(1)}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateGeoSectionRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _update.Handle(id, request, cancellationToken));

    /// <summary>Soft-delete a leaf section while retaining historical references.</summary>
    [HttpDelete("{id:int:min(1)}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        FromResult(await _delete.Handle(
            id,
            HttpContext.GetApiKeyIdentity()!.ApiKeyId,
            cancellationToken));
}
