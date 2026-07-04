using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Billing;

[ApiController]
[Route("balances")]
public sealed class BalancesController : BaseController
{
    private readonly GetBalanceHandler _get;
    private readonly TopUpHandler _topUp;
    private readonly ListTransactionsHandler _transactions;

    public BalancesController(GetBalanceHandler get, TopUpHandler topUp, ListTransactionsHandler transactions)
    {
        _get = get;
        _topUp = topUp;
        _transactions = transactions;
    }

    /// <summary>Get a customer's current prepaid balance.</summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] short customerId, CancellationToken cancellationToken) =>
        FromResult(await _get.Handle(customerId, cancellationToken));

    /// <summary>List a customer's money-ledger entries.</summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> Transactions([FromQuery] short customerId, CancellationToken cancellationToken) =>
        FromResult(await _transactions.Handle(customerId, cancellationToken));

    /// <summary>Credit a customer's balance and record a ledger entry.</summary>
    [HttpPost("top-up")]
    public async Task<IActionResult> TopUp([FromBody] TopUpRequest request, CancellationToken cancellationToken) =>
        FromResult(await _topUp.Handle(request, cancellationToken));
}
