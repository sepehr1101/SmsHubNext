using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

[ApiController]
[Route("customers")]
public sealed class CustomersController : ControllerBase
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
        (await _list.Handle(cancellationToken)).ToActionResult();

    /// <summary>Create a customer.</summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateCustomerRequest request,
        CancellationToken cancellationToken) =>
        (await _create.Handle(request, cancellationToken)).ToActionResult(StatusCodes.Status201Created);
}
