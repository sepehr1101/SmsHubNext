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
    private readonly RetryDispatchHandler _retryDispatch;

    public BatchesController(
        GetBatchHandler get,
        ListBatchMessagesHandler messages,
        ListBatchEventsHandler events,
        RetryDispatchHandler retryDispatch)
    {
        _get = get;
        _messages = messages;
        _events = events;
        _retryDispatch = retryDispatch;
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

    /// <summary>Manually re-enter a dispatch-failed batch into the dispatch queue.</summary>
    [HttpPost("{id:long}/retry-dispatch")]
    public async Task<IActionResult> RetryDispatch(long id, CancellationToken cancellationToken) =>
        (await _retryDispatch.Handle(id, cancellationToken)).ToActionResult();
}
