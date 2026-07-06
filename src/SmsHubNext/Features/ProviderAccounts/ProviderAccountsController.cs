using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SmsHubNext.Shared.Http;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ProviderAccounts;

[ApiController]
[Route("provider-accounts")]
public sealed class ProviderAccountsController : BaseController
{
    private readonly ListProviderAccountsHandler _list;
    private readonly GetProviderAccountHandler _get;
    private readonly CreateProviderAccountHandler _create;
    private readonly UpdateProviderAccountHandler _update;

    public ProviderAccountsController(
        ListProviderAccountsHandler list,
        GetProviderAccountHandler get,
        CreateProviderAccountHandler create,
        UpdateProviderAccountHandler update)
    {
        _list = list;
        _get = get;
        _create = create;
        _update = update;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) =>
        FromResult(await _list.Handle(cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken cancellationToken) =>
        FromResult(await _get.Handle(id, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateProviderAccountRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _create.Handle(request, cancellationToken), StatusCodes.Status201Created);

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        [FromBody] UpdateProviderAccountRequest request,
        CancellationToken cancellationToken) =>
        FromResult(await _update.Handle(id, request, cancellationToken));
}
