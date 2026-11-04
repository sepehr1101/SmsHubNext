using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;

namespace SmsHubNext.Features.Logging;

[ApiController]
[Authorize(Policy = AuthenticationPolicies.Administrator)]
[Route("logging")]
public sealed class LoggingController(ListHttpErrorsHandler handler) : BaseController
{
    /// <summary>HTTP 4xx/5xx responses by Tehran calendar dates and optional daily hours.</summary>
    [HttpGet("errors")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Errors([FromQuery] HttpErrorQuery query, CancellationToken cancellationToken) =>
        FromResult(await handler.Handle(query, cancellationToken));
}
