using SmsHubNext.App.Shared.Api;

namespace SmsHubNext.App.Features.MessageTypes;

public sealed class MessageTypesService
{
    private readonly ManagementApiClient _api;

    public MessageTypesService(ManagementApiClient api) => _api = api;

    public Task<ApiClientResult<IReadOnlyList<MessageTypeItem>>> List(CancellationToken cancellationToken) =>
        _api.Get<IReadOnlyList<MessageTypeItem>>("reference-data/message-types", cancellationToken);

    public Task<ApiClientResult<CreateMessageTypeResponse>> Create(
        CreateMessageTypeRequest request,
        CancellationToken cancellationToken) =>
        _api.Post<CreateMessageTypeRequest, CreateMessageTypeResponse>("reference-data/message-types", request, cancellationToken);

    public Task<ApiClientResult<string?>> Update(
        byte id,
        UpdateMessageTypeRequest request,
        CancellationToken cancellationToken) =>
        _api.Put($"reference-data/message-types/{id}", request, cancellationToken);

    public Task<ApiClientResult<string?>> Delete(byte id, CancellationToken cancellationToken) =>
        _api.Delete($"reference-data/message-types/{id}", cancellationToken);
}
