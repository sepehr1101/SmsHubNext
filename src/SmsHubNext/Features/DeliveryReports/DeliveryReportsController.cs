using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.DeliveryReports;

[ApiController]
[Route("delivery-reports")]
public sealed class DeliveryReportsController : ControllerBase
{
    private readonly IngestDeliveryReportHandler _ingest;
    private readonly ListDeliveryReportsHandler _list;

    public DeliveryReportsController(IngestDeliveryReportHandler ingest, ListDeliveryReportsHandler list)
    {
        _ingest = ingest;
        _list = list;
    }

    /// <summary>Ingest a delivery report and update the message's current delivery status.</summary>
    [HttpPost]
    public async Task<IActionResult> Ingest(
        [FromBody] IngestDeliveryReportRequest request,
        CancellationToken cancellationToken) =>
        (await _ingest.Handle(request, cancellationToken)).ToActionResult(StatusCodes.Status201Created);

    /// <summary>List a message's full status-event history.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] long messageId, CancellationToken cancellationToken) =>
        (await _list.Handle(messageId, cancellationToken)).ToActionResult();
}
