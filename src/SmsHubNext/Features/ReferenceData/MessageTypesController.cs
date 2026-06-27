using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

[ApiController]
[Route("api/reference-data/message-types")]
public sealed class MessageTypesController : ControllerBase
{
    private readonly ListMessageTypesHandler _handler;

    public MessageTypesController(ListMessageTypesHandler handler) => _handler = handler;

    /// <summary>List the message-type classifications.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await _handler.Handle(cancellationToken)).ToActionResult();
}
