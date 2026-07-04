using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Inbound;

[ApiController]
[Route("inbound-messages")]
public sealed class InboundController : BaseController
{
    private readonly ListInboundMessagesHandler _list;

    public InboundController(ListInboundMessagesHandler list) => _list = list;

    /// <summary>List received (MO) messages, newest first; optionally filter by the receiving number.</summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? recipientNumber,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default) =>
        FromResult(await _list.Handle(recipientNumber, take, cancellationToken));
}
