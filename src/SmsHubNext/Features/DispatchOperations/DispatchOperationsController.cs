using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.DispatchOperations;

[ApiController]
[Route("dispatch/operations")]
public sealed class DispatchOperationsController : ControllerBase
{
    private readonly DispatchOperationsHandler _handler;

    public DispatchOperationsController(DispatchOperationsHandler handler) => _handler = handler;

    /// <summary>Operational totals for the database-backed dispatch queue.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] DispatchOperationsRequest request,
        CancellationToken cancellationToken) =>
        (await _handler.Summary(request, cancellationToken)).ToActionResult();

    /// <summary>Lightweight batch list for monitoring held, failed, retrying, and awaiting-confirmation work.</summary>
    [HttpGet("batches")]
    public async Task<IActionResult> Batches(
        [FromQuery] DispatchOperationsRequest request,
        CancellationToken cancellationToken) =>
        (await _handler.Batches(request, cancellationToken)).ToActionResult();
}
