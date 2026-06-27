using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Tariffs;

[ApiController]
[Route("tariffs")]
public sealed class TariffsController : ControllerBase
{
    private readonly ListTariffsHandler _handler;

    public TariffsController(ListTariffsHandler handler) => _handler = handler;

    /// <summary>List tariffs with their price bands.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await _handler.Handle(cancellationToken)).ToActionResult();
}
