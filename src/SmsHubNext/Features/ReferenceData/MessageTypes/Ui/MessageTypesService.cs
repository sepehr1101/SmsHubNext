using SmsHubNext.Shared.Ui;
using MessageTypeItem = SmsHubNext.Features.ReferenceData.MessageTypes.MessageType;
using CreateMessageTypeRequest = SmsHubNext.Features.ReferenceData.MessageTypes.CreateMessageTypeRequest;
using CreateMessageTypeResponse = SmsHubNext.Features.ReferenceData.MessageTypes.CreateMessageTypeResponse;
using UpdateMessageTypeRequest = SmsHubNext.Features.ReferenceData.MessageTypes.UpdateMessageTypeRequest;

namespace SmsHubNext.Features.ReferenceData.MessageTypes.Ui;

public sealed class MessageTypesService(
    UiOperations operations,
    ListMessageTypesHandler listMessageTypesHandler,
    CreateMessageTypeHandler createMessageTypeHandler,
    UpdateMessageTypeHandler updateMessageTypeHandler,
    DeleteMessageTypeHandler deleteMessageTypeHandler)
{
    public Task<UiResult<IReadOnlyList<MessageTypeItem>>> List(CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listMessageTypesHandler.Handle(cancellationToken), token);

    public Task<UiResult<CreateMessageTypeResponse>> Create(CreateMessageTypeRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => createMessageTypeHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<string?>> Update(byte id, UpdateMessageTypeRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => updateMessageTypeHandler.Handle(id, request, cancellationToken), token);

    public Task<UiResult<string?>> Delete(byte id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => deleteMessageTypeHandler.Handle(id, identity.ApiKeyId, cancellationToken), token);

}
