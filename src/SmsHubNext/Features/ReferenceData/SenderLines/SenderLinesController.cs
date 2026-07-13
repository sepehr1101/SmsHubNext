using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.SenderLines;

[ApiController]
[Route("reference-data/sender-lines")]
public sealed class SenderLinesController : BaseController
{
    private readonly ListSenderLinesHandler _list;
    private readonly CreateSenderLineHandler _create;
    private readonly AssignProviderAccountHandler _assignProviderAccount;
    private readonly UpdateSenderLineHandler _update;
    private readonly DeleteSenderLineHandler _delete;

    public SenderLinesController(
        ListSenderLinesHandler list,
        CreateSenderLineHandler create,
        AssignProviderAccountHandler assignProviderAccount,
        UpdateSenderLineHandler update,
        DeleteSenderLineHandler delete)
    {
        _list = list;
        _create = create;
        _assignProviderAccount = assignProviderAccount;
        _update = update;
        _delete = delete;
    }

    /// <summary>List the sending lines.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        FromResult(await _list.Handle(cancellationToken));

    /// <summary>Register a sending line for a provider.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateSenderLineRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _create.Handle(request, cancellationToken), StatusCodes.Status201Created);

    /// <summary>Change or clear the provider account that owns a sending line.</summary>
    [HttpPut("{id:int:range(1,32767)}/provider-account")]
    public async Task<IActionResult> AssignProviderAccount(
        short id,
        [FromBody] AssignProviderAccountRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _assignProviderAccount.Handle(id, request, cancellationToken));

    /// <summary>Update a sender line; its provider remains immutable.</summary>
    [HttpPut("{id:int:range(1,32767)}")]
    public async Task<IActionResult> Update(
        short id,
        [FromBody] UpdateSenderLineRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _update.Handle(id, request, cancellationToken));

    /// <summary>Soft-delete a sender line while retaining accepted message history.</summary>
    [HttpDelete("{id:int:range(1,32767)}")]
    public async Task<IActionResult> Delete(short id, CancellationToken cancellationToken) =>
        FromResult(await _delete.Handle(
            id,
            HttpContext.GetApiKeyIdentity()!.ApiKeyId,
            cancellationToken));
}
