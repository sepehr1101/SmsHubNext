using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    public SenderLinesController(
        ListSenderLinesHandler list,
        CreateSenderLineHandler create,
        AssignProviderAccountHandler assignProviderAccount)
    {
        _list = list;
        _create = create;
        _assignProviderAccount = assignProviderAccount;
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
    [HttpPut("{id:short}/provider-account")]
    public async Task<IActionResult> AssignProviderAccount(
        short id,
        [FromBody] AssignProviderAccountRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _assignProviderAccount.Handle(id, request, cancellationToken));
}
