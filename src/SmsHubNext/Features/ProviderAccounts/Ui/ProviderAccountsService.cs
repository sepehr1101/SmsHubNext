using SmsHubNext.Shared.Ui;
using ProviderAccountItem = SmsHubNext.Features.ProviderAccounts.ProviderAccount;
using CreateProviderAccountRequest = SmsHubNext.Features.ProviderAccounts.CreateProviderAccountRequest;
using CreateProviderAccountResponse = SmsHubNext.Features.ProviderAccounts.CreateProviderAccountResponse;
using UpdateProviderAccountRequest = SmsHubNext.Features.ProviderAccounts.UpdateProviderAccountRequest;

namespace SmsHubNext.Features.ProviderAccounts.Ui;

public sealed class ProviderAccountsService(
    UiOperations operations,
    ListProviderAccountsHandler listProviderAccountsHandler,
    GetProviderAccountHandler getProviderAccountHandler,
    CreateProviderAccountHandler createProviderAccountHandler,
    UpdateProviderAccountHandler updateProviderAccountHandler,
    DeleteProviderAccountHandler deleteProviderAccountHandler)
{
    public Task<UiResult<IReadOnlyList<ProviderAccountItem>>> List(CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listProviderAccountsHandler.Handle(cancellationToken), token);

    public Task<UiResult<ProviderAccountItem>> Get(int id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => getProviderAccountHandler.Handle(id, cancellationToken), token);

    public Task<UiResult<CreateProviderAccountResponse>> Create(CreateProviderAccountRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => createProviderAccountHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<string?>> Update(int id, UpdateProviderAccountRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => updateProviderAccountHandler.Handle(id, request, cancellationToken), token);

    public Task<UiResult<string?>> Delete(int id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => deleteProviderAccountHandler.Handle(id, identity.ApiKeyId, cancellationToken), token);

}
