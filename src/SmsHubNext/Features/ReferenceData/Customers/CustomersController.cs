using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Features.Authentication;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Customers;

[ApiController]
[Route("customers")]
public sealed class CustomersController : BaseController
{
    private readonly CreateCustomerHandler _create;
    private readonly ListCustomersHandler _list;
    private readonly UpdateCustomerHandler _update;
    private readonly DeleteCustomerHandler _delete;

    public CustomersController(
        CreateCustomerHandler create,
        ListCustomersHandler list,
        UpdateCustomerHandler update,
        DeleteCustomerHandler delete)
    {
        _create = create;
        _list = list;
        _update = update;
        _delete = delete;
    }

    /// <summary>List customers.</summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        FromResult(await _list.Handle(cancellationToken));

    /// <summary>Create a customer.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCustomerRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _create.Handle(request, cancellationToken), StatusCodes.Status201Created);

    /// <summary>Update a customer that has not been deleted.</summary>
    [HttpPut("{id:int:range(1,32767)}")]
    public async Task<IActionResult> Update(
        short id,
        [FromBody] UpdateCustomerRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _update.Handle(id, request, cancellationToken));

    /// <summary>Soft-delete a customer while retaining historical references.</summary>
    [HttpDelete("{id:int:range(1,32767)}")]
    public async Task<IActionResult> Delete(short id, CancellationToken cancellationToken) =>
        FromResult(await _delete.Handle(
            id,
            HttpContext.GetApiKeyIdentity()!.ApiKeyId,
            cancellationToken));
}
