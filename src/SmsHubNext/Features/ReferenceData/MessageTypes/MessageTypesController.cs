using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.MessageTypes;

[ApiController]
[Route("reference-data/message-types")]
public sealed class MessageTypesController : BaseController
{
    private readonly ListMessageTypesHandler _list;
    private readonly CreateMessageTypeHandler _create;
    private readonly UpdateMessageTypeHandler _update;
    private readonly DeleteMessageTypeHandler _delete;

    public MessageTypesController(
        ListMessageTypesHandler list,
        CreateMessageTypeHandler create,
        UpdateMessageTypeHandler update,
        DeleteMessageTypeHandler delete)
    {
        _list = list;
        _create = create;
        _update = update;
        _delete = delete;
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

    /// <summary>Update a classification name/status; its stable id and code are immutable.</summary>
    [HttpPut("{id:int:range(1,255)}")]
    public async Task<IActionResult> Update(
        byte id,
        [FromBody] UpdateMessageTypeRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _update.Handle(id, request, cancellationToken));

    /// <summary>Soft-delete a classification while retaining historical references.</summary>
    [HttpDelete("{id:int:range(1,255)}")]
    public async Task<IActionResult> Delete(byte id, CancellationToken cancellationToken) =>
        FromResult(await _delete.Handle(
            id,
            HttpContext.GetApiKeyIdentity()!.ApiKeyId,
            cancellationToken));
}
