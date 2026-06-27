using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Billing;

[ApiController]
[Route("balances")]
public sealed class BalancesController : ControllerBase
{
    private readonly GetBalanceHandler _get;
    private readonly TopUpHandler _topUp;

    public BalancesController(GetBalanceHandler get, TopUpHandler topUp)
    {
        _get = get;
        _topUp = topUp;
    }

    /// <summary>Get a customer's current prepaid balance.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] short customerId, CancellationToken cancellationToken) =>
        (await _get.Handle(customerId, cancellationToken)).ToActionResult();

    /// <summary>Credit a customer's balance and record a ledger entry.</summary>
    [HttpPost("top-up")]
    public async Task<IActionResult> TopUp([FromBody] TopUpRequest request, CancellationToken cancellationToken) =>
        (await _topUp.Handle(request, cancellationToken)).ToActionResult();
}
