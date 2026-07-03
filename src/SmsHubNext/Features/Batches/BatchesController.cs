using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Batches;

[ApiController]
[Route("batches")]
public sealed class BatchesController : ControllerBase
{
    private readonly GetBatchHandler _get;
    private readonly ListBatchMessagesHandler _messages;
    private readonly ListBatchEventsHandler _events;

    public BatchesController(GetBatchHandler get, ListBatchMessagesHandler messages, ListBatchEventsHandler events)
    {
        _get = get;
        _messages = messages;
        _events = events;
    }

    /// <summary>Get a batch header and its current dispatch status.</summary>
    [HttpGet("{id:long}")]
    public async Task<IActionResult> Get(long id, CancellationToken cancellationToken) =>
        (await _get.Handle(id, cancellationToken)).ToActionResult();

    /// <summary>List the messages in a batch with their send/delivery status.</summary>
    [HttpGet("{id:long}/messages")]
    public async Task<IActionResult> Messages(long id, CancellationToken cancellationToken) =>
        (await _messages.Handle(id, cancellationToken)).ToActionResult();

    /// <summary>List the operational timeline for a batch.</summary>
    [HttpGet("{id:long}/events")]
    public async Task<IActionResult> Events(long id, CancellationToken cancellationToken) =>
        (await _events.Handle(id, cancellationToken)).ToActionResult();
}
