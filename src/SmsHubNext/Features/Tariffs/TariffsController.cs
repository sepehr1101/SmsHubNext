using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Tariffs;

[ApiController]
[Route("tariffs")]
public sealed class TariffsController : BaseController
{
    private readonly ListTariffsHandler _list;
    private readonly QuoteHandler _quote;
    private readonly CreateTariffHandler _create;
    private readonly UpdateTariffHandler _update;
    private readonly DeleteTariffHandler _delete;

    public TariffsController(
        ListTariffsHandler list,
        QuoteHandler quote,
        CreateTariffHandler create,
        UpdateTariffHandler update,
        DeleteTariffHandler delete)
    {
        _list = list;
        _quote = quote;
        _create = create;
        _update = update;
        _delete = delete;
    }

    /// <summary>List tariffs with their price bands.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        FromResult(await _list.Handle(cancellationToken));

    /// <summary>Price a message: encoding, segments, and resolved cost.</summary>
    [HttpPost("quote")]
    public async Task<IActionResult> Quote([FromBody] QuoteRequest request, CancellationToken cancellationToken) =>
        FromResult(await _quote.Handle(request, cancellationToken));

    /// <summary>Create a versioned tariff with immutable price bands.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateTariffRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _create.Handle(request, cancellationToken), StatusCodes.Status201Created);

    /// <summary>Close, reopen, activate, or deactivate a tariff version.</summary>
    [HttpPut("{id:int:min(1)}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateTariffRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _update.Handle(id, request, cancellationToken));

    /// <summary>Soft-delete a tariff version while retaining message snapshots and FK history.</summary>
    [HttpDelete("{id:int:min(1)}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken) =>
        FromResult(await _delete.Handle(
            id,
            HttpContext.GetApiKeyIdentity()!.ApiKeyId,
            cancellationToken));
}
