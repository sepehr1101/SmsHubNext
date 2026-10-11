using SmsHubNext.App.Shared.Api;

namespace SmsHubNext.App.Features.ProviderAccounts;

public sealed class ProviderAccountsService
{
    private readonly ManagementApiClient _api;
    public ProviderAccountsService(ManagementApiClient api) => _api = api;

    public Task<ApiClientResult<IReadOnlyList<ProviderAccountItem>>> List(CancellationToken cancellationToken) =>
        _api.Get<IReadOnlyList<ProviderAccountItem>>("provider-accounts", cancellationToken);

    public Task<ApiClientResult<ProviderAccountItem>> Get(int id, CancellationToken cancellationToken) =>
        _api.Get<ProviderAccountItem>($"provider-accounts/{id}", cancellationToken);

    public Task<ApiClientResult<CreateProviderAccountResponse>> Create(CreateProviderAccountRequest request, CancellationToken cancellationToken) =>
        _api.Post<CreateProviderAccountRequest, CreateProviderAccountResponse>("provider-accounts", request, cancellationToken);

    public Task<ApiClientResult<string?>> Update(int id, UpdateProviderAccountRequest request, CancellationToken cancellationToken) =>
        _api.Put($"provider-accounts/{id}", request, cancellationToken);

    public Task<ApiClientResult<string?>> Delete(int id, CancellationToken cancellationToken) =>
        _api.Delete($"provider-accounts/{id}", cancellationToken);
}
