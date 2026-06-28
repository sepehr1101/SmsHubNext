using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Batches;

[ApiController]
[Route("batches")]
public sealed class BatchesController : ControllerBase
{
    private readonly GetBatchHandler _get;
    private readonly ListBatchMessagesHandler _messages;

    public BatchesController(GetBatchHandler get, ListBatchMessagesHandler messages)
    {
        _get = get;
        _messages = messages;
    }

    /// <summary>Get a batch header and its current dispatch status.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken) =>
        (await _get.Handle(id, cancellationToken)).ToActionResult();

    /// <summary>List the messages in a batch with their send/delivery status.</summary>
    [HttpGet("{id:long}/messages")]
    public async Task<IActionResult> Messages(long id, CancellationToken cancellationToken) =>
        (await _messages.Handle(id, cancellationToken)).ToActionResult();
}
