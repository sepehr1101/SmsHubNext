using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

[ApiController]
[Route("reference-data/sender-lines")]
public sealed class SenderLinesController : ControllerBase
{
    private readonly ListSenderLinesHandler _handler;

    public SenderLinesController(ListSenderLinesHandler handler) => _handler = handler;

    /// <summary>List the sending lines.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await _handler.Handle(cancellationToken)).ToActionResult();
}
