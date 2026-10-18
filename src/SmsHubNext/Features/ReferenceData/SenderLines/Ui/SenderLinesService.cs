using SmsHubNext.Shared.Ui;
using SenderLineItem = SmsHubNext.Features.ReferenceData.SenderLines.SenderLine;
using CreateSenderLineRequest = SmsHubNext.Features.ReferenceData.SenderLines.CreateSenderLineRequest;
using CreateSenderLineResponse = SmsHubNext.Features.ReferenceData.SenderLines.CreateSenderLineResponse;
using UpdateSenderLineRequest = SmsHubNext.Features.ReferenceData.SenderLines.UpdateSenderLineRequest;

namespace SmsHubNext.Features.ReferenceData.SenderLines.Ui;

public sealed class SenderLinesService(
    UiOperations operations,
    ListSenderLinesHandler listSenderLinesHandler,
    CreateSenderLineHandler createSenderLineHandler,
    UpdateSenderLineHandler updateSenderLineHandler,
    DeleteSenderLineHandler deleteSenderLineHandler)
{
    public Task<UiResult<IReadOnlyList<SenderLineItem>>> List(CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listSenderLinesHandler.Handle(cancellationToken), token);

    public Task<UiResult<CreateSenderLineResponse>> Create(CreateSenderLineRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => createSenderLineHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<string?>> Update(short id, UpdateSenderLineRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => updateSenderLineHandler.Handle(id, request, cancellationToken), token);

    public Task<UiResult<string?>> Delete(short id, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => deleteSenderLineHandler.Handle(id, identity.ApiKeyId, cancellationToken), token);

}
