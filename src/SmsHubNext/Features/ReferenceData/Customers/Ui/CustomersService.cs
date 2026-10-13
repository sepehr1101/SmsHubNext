using SmsHubNext.Shared.Ui;
using CustomerItem = SmsHubNext.Features.ReferenceData.Customers.Customer;
using CreateCustomerRequest = SmsHubNext.Features.ReferenceData.Customers.CreateCustomerRequest;
using CreateCustomerResponse = SmsHubNext.Features.ReferenceData.Customers.CreateCustomerResponse;
using UpdateCustomerRequest = SmsHubNext.Features.ReferenceData.Customers.UpdateCustomerRequest;

namespace SmsHubNext.Features.ReferenceData.Customers.Ui;

public sealed class CustomersService(
    UiOperations operations,
    ListCustomersHandler listCustomersHandler,
    CreateCustomerHandler createCustomerHandler,
    UpdateCustomerHandler updateCustomerHandler,
    DeleteCustomerHandler deleteCustomerHandler)
{
    public Task<UiResult<IReadOnlyList<CustomerItem>>> List(CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listCustomersHandler.Handle(cancellationToken), token);

    public Task<UiResult<CreateCustomerResponse>> Create(CreateCustomerRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => createCustomerHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<string?>> Update(short id, UpdateCustomerRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => updateCustomerHandler.Handle(id, request, cancellationToken), token);

    public Task<UiResult<string?>> Delete(short id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => deleteCustomerHandler.Handle(id, identity.ApiKeyId, cancellationToken), token);

}
