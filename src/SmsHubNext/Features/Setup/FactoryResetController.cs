using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Http;

namespace SmsHubNext.Features.Setup;

[ApiController]
[Authorize]
[Route("setup")]
public sealed class FactoryResetController : BaseController
{
    private readonly FactoryResetHandler _handler;

    public FactoryResetController(FactoryResetHandler handler) => _handler = handler;

    /// <summary>
    /// Removes all installation-specific data and restores an empty first-install state. The operation is
    /// permanently rejected after the first SMS has been accepted.
    /// </summary>
    [HttpPost("factory-reset")]
    public async Task<IActionResult> Reset(
        [FromBody] FactoryResetRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _handler.Handle(request, cancellationToken));
}
