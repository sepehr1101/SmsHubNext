using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Reports;

[ApiController]
[Route("reports/messages")]
public sealed class ReportsController : ControllerBase
{
    private readonly MessageReportsHandler _handler;

    public ReportsController(MessageReportsHandler handler) => _handler = handler;

    /// <summary>Message totals for a Jalali period, with optional dimension filters.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] MessageReportRequest request,
        CancellationToken cancellationToken) =>
        (await _handler.Summary(request, cancellationToken)).ToActionResult();

    /// <summary>Message totals grouped by provider.</summary>
    [HttpGet("by-provider")]
    public async Task<IActionResult> ByProvider(
        [FromQuery] MessageReportRequest request,
        CancellationToken cancellationToken) =>
        (await _handler.ByProvider(request, cancellationToken)).ToActionResult();

    /// <summary>Message totals grouped by message type.</summary>
    [HttpGet("by-message-type")]
    public async Task<IActionResult> ByMessageType(
        [FromQuery] MessageReportRequest request,
        CancellationToken cancellationToken) =>
        (await _handler.ByMessageType(request, cancellationToken)).ToActionResult();

    /// <summary>Message totals grouped by assigned geographic section.</summary>
    [HttpGet("by-geo")]
    public async Task<IActionResult> ByGeo(
        [FromQuery] MessageReportRequest request,
        CancellationToken cancellationToken) =>
        (await _handler.ByGeo(request, cancellationToken)).ToActionResult();
}
