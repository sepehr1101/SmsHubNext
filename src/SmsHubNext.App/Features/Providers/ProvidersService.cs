using SmsHubNext.App.Shared.Api;

namespace SmsHubNext.App.Features.Providers;

public sealed class ProvidersService
{
    private readonly ManagementApiClient _api;

    public ProvidersService(ManagementApiClient api) => _api = api;

    public Task<ApiClientResult<IReadOnlyList<ProviderItem>>> List(CancellationToken cancellationToken) =>
        _api.Get<IReadOnlyList<ProviderItem>>("reference-data/providers", cancellationToken);

    public Task<ApiClientResult<CreateProviderResponse>> Create(
        CreateProviderRequest request,
        CancellationToken cancellationToken) =>
        _api.Post<CreateProviderRequest, CreateProviderResponse>("reference-data/providers", request, cancellationToken);

    public Task<ApiClientResult<string?>> Update(
        byte id,
        UpdateProviderRequest request,
        CancellationToken cancellationToken) =>
        _api.Put($"reference-data/providers/{id}", request, cancellationToken);

    public Task<ApiClientResult<string?>> Delete(byte id, CancellationToken cancellationToken) =>
        _api.Delete($"reference-data/providers/{id}", cancellationToken);
}
