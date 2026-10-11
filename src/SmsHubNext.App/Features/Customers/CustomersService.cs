using SmsHubNext.App.Shared.Api;

namespace SmsHubNext.App.Features.Customers;

public sealed class CustomersService
{
    private readonly ManagementApiClient _api;

    public CustomersService(ManagementApiClient api) => _api = api;

    public Task<ApiClientResult<IReadOnlyList<CustomerItem>>> List(CancellationToken cancellationToken) =>
        _api.Get<IReadOnlyList<CustomerItem>>("customers", cancellationToken);

    public Task<ApiClientResult<CreateCustomerResponse>> Create(
        CreateCustomerRequest request,
        CancellationToken cancellationToken) =>
        _api.Post<CreateCustomerRequest, CreateCustomerResponse>("customers", request, cancellationToken);

    public Task<ApiClientResult<string?>> Update(
        short id,
        UpdateCustomerRequest request,
        CancellationToken cancellationToken) =>
        _api.Put($"customers/{id}", request, cancellationToken);

    public Task<ApiClientResult<string?>> Delete(short id, CancellationToken cancellationToken) =>
        _api.Delete($"customers/{id}", cancellationToken);
}
