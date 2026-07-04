using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

[ApiController]
[Route("customers")]
public sealed class CustomersController : BaseController
{
    private readonly CreateCustomerHandler _create;
    private readonly ListCustomersHandler _list;

    public CustomersController(CreateCustomerHandler create, ListCustomersHandler list)
    {
        _create = create;
        _list = list;
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
}
