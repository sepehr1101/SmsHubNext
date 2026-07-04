using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Reports;

[ApiController]
[Route("reports/messages")]
public sealed class ReportsController : BaseController
{
    private readonly MessageReportsHandler _handler;
    private readonly ApiKeyAuthenticator _authenticator;

    public ReportsController(MessageReportsHandler handler, ApiKeyAuthenticator authenticator)
    {
        _handler = handler;
        _authenticator = authenticator;
    }

    /// <summary>Message totals for a Jalali period, with optional dimension filters.</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> Summary(
        [FromQuery] MessageReportRequest request,
        CancellationToken cancellationToken) =>
        await HandleScopedReport(request, cancellationToken, _handler.Summary);

    /// <summary>Message totals grouped by provider.</summary>
    [HttpGet("by-provider")]
    public async Task<IActionResult> ByProvider(
        [FromQuery] MessageReportRequest request,
        CancellationToken cancellationToken) =>
        await HandleScopedReport(request, cancellationToken, _handler.ByProvider);

    /// <summary>Message totals grouped by message type.</summary>
    [HttpGet("by-message-type")]
    public async Task<IActionResult> ByMessageType(
        [FromQuery] MessageReportRequest request,
        CancellationToken cancellationToken) =>
        await HandleScopedReport(request, cancellationToken, _handler.ByMessageType);

    /// <summary>Message totals grouped by assigned geographic section.</summary>
    [HttpGet("by-geo")]
    public async Task<IActionResult> ByGeo(
        [FromQuery] MessageReportRequest request,
        CancellationToken cancellationToken) =>
        await HandleScopedReport(request, cancellationToken, _handler.ByGeo);

    /// <summary>Message totals grouped by Jalali month.</summary>
    [HttpGet("by-jalali-month")]
    public async Task<IActionResult> ByJalaliMonth(
        [FromQuery] MessageReportRequest request,
        CancellationToken cancellationToken) =>
        await HandleScopedReport(request, cancellationToken, _handler.ByJalaliMonth);

    /// <summary>Message totals rolled up by province/city/zone ancestors.</summary>
    [HttpGet("by-geo-rollup")]
    public async Task<IActionResult> ByGeoRollup(
        [FromQuery] MessageReportRequest request,
        CancellationToken cancellationToken) =>
        await HandleScopedReport(request, cancellationToken, _handler.ByGeoRollup);

    /// <summary>Message totals grouped by provider, message type, and assigned geo section.</summary>
    [HttpGet("by-provider-message-type-geo")]
    public async Task<IActionResult> ByProviderMessageTypeGeo(
        [FromQuery] MessageReportRequest request,
        CancellationToken cancellationToken) =>
        await HandleScopedReport(request, cancellationToken, _handler.ByProviderMessageTypeGeo);

    private async Task<IActionResult> HandleScopedReport<T>(
        MessageReportRequest request,
        CancellationToken cancellationToken,
        Func<MessageReportRequest, CancellationToken, Task<Result<T>>> handle)
    {
        Result<MessageReportRequest> scoped = await ScopeToApiKeyCustomer(request, cancellationToken);
        if (scoped.IsFailure)
            return FromResult(scoped);

        return FromResult(await handle(scoped.Value, cancellationToken));
    }

    private async Task<Result<MessageReportRequest>> ScopeToApiKeyCustomer(
        MessageReportRequest request,
        CancellationToken cancellationToken)
    {
        string rawKey = Request.Headers[ApiKeyConstants.HeaderName].ToString();
        if (string.IsNullOrWhiteSpace(rawKey))
            return request;

        Result<ApiKeyIdentity> identity = await _authenticator.Authenticate(
            rawKey,
            HttpContext.Connection.RemoteIpAddress,
            cancellationToken);
        if (identity.IsFailure)
            return identity.Error!;

        if (request.CustomerId is not null && request.CustomerId != identity.Value.CustomerId)
            return Error.Validation(
                "reports.customer_mismatch",
                "The requested customer does not match the authenticated API key.");

        return request.ForCustomer(identity.Value.CustomerId);
    }
}
