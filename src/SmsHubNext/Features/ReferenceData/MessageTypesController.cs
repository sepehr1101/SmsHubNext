using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

[ApiController]
[Route("reference-data/message-types")]
public sealed class MessageTypesController : ControllerBase
{
    private readonly ListMessageTypesHandler _list;
    private readonly CreateMessageTypeHandler _create;

    public MessageTypesController(ListMessageTypesHandler list, CreateMessageTypeHandler create)
    {
        _list = list;
        _create = create;
    }

    /// <summary>List the message-type classifications.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await _list.Handle(cancellationToken)).ToActionResult();

    /// <summary>Register a message-type classification.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateMessageTypeRequest request,
        CancellationToken cancellationToken) =>
        (await _create.Handle(request, cancellationToken)).ToActionResult(StatusCodes.Status201Created);
}
