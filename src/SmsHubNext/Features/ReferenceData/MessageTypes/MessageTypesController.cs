using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.MessageTypes;

[ApiController]
[Route("reference-data/message-types")]
public sealed class MessageTypesController : BaseController
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
        FromResult(await _list.Handle(cancellationToken));

    /// <summary>Register a message-type classification.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateMessageTypeRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _create.Handle(request, cancellationToken), StatusCodes.Status201Created);
}
