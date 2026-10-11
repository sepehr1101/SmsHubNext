using SmsHubNext.App.Shared.Api;

namespace SmsHubNext.App.Features.SenderLines;

public sealed class SenderLinesService
{
    private readonly ManagementApiClient _api;
    public SenderLinesService(ManagementApiClient api) => _api = api;

    public Task<ApiClientResult<IReadOnlyList<SenderLineItem>>> List(CancellationToken cancellationToken) =>
        _api.Get<IReadOnlyList<SenderLineItem>>("reference-data/sender-lines", cancellationToken);

    public Task<ApiClientResult<CreateSenderLineResponse>> Create(CreateSenderLineRequest request, CancellationToken cancellationToken) =>
        _api.Post<CreateSenderLineRequest, CreateSenderLineResponse>("reference-data/sender-lines", request, cancellationToken);

    public Task<ApiClientResult<string?>> Update(short id, UpdateSenderLineRequest request, CancellationToken cancellationToken) =>
        _api.Put($"reference-data/sender-lines/{id}", request, cancellationToken);

    public Task<ApiClientResult<string?>> Delete(short id, CancellationToken cancellationToken) =>
        _api.Delete($"reference-data/sender-lines/{id}", cancellationToken);
}
