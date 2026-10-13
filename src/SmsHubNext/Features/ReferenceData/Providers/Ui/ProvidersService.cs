using SmsHubNext.Shared.Ui;
using ProviderItem = SmsHubNext.Features.ReferenceData.Providers.Provider;
using CreateProviderRequest = SmsHubNext.Features.ReferenceData.Providers.CreateProviderRequest;
using CreateProviderResponse = SmsHubNext.Features.ReferenceData.Providers.CreateProviderResponse;
using UpdateProviderRequest = SmsHubNext.Features.ReferenceData.Providers.UpdateProviderRequest;

namespace SmsHubNext.Features.ReferenceData.Providers.Ui;

public sealed class ProvidersService(
    UiOperations operations,
    ListProvidersHandler listProvidersHandler,
    CreateProviderHandler createProviderHandler,
    UpdateProviderHandler updateProviderHandler,
    DeleteProviderHandler deleteProviderHandler)
{
    public Task<UiResult<IReadOnlyList<ProviderItem>>> List(CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listProvidersHandler.Handle(cancellationToken), token);

    public Task<UiResult<CreateProviderResponse>> Create(CreateProviderRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => createProviderHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<string?>> Update(byte id, UpdateProviderRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => updateProviderHandler.Handle(id, request, cancellationToken), token);

    public Task<UiResult<string?>> Delete(byte id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => deleteProviderHandler.Handle(id, identity.ApiKeyId, cancellationToken), token);

}
