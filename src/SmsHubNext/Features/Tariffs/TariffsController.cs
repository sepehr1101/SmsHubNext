using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Tariffs;

[ApiController]
[Route("tariffs")]
public sealed class TariffsController : ControllerBase
{
    private readonly ListTariffsHandler _list;
    private readonly QuoteHandler _quote;

    public TariffsController(ListTariffsHandler list, QuoteHandler quote)
    {
        _list = list;
        _quote = quote;
    }

    /// <summary>List tariffs with their price bands.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        (await _list.Handle(cancellationToken)).ToActionResult();

    /// <summary>Price a message: encoding, segments, and resolved cost.</summary>
    [HttpPost("quote")]
    public async Task<IActionResult> Quote([FromBody] QuoteRequest request, CancellationToken cancellationToken) =>
        (await _quote.Handle(request, cancellationToken)).ToActionResult();
}
