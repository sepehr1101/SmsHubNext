using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

[ApiController]
[Route("reference-data/sender-lines")]
public sealed class SenderLinesController : ControllerBase
{
    private readonly ListSenderLinesHandler _list;
    private readonly CreateSenderLineHandler _create;

    public SenderLinesController(ListSenderLinesHandler list, CreateSenderLineHandler create)
    {
        _list = list;
        _create = create;
    }

    /// <summary>List the sending lines.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await _list.Handle(cancellationToken)).ToActionResult();

    /// <summary>Register a sending line for a provider.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateSenderLineRequest request,
        CancellationToken cancellationToken) =>
        (await _create.Handle(request, cancellationToken)).ToActionResult(StatusCodes.Status201Created);
}
