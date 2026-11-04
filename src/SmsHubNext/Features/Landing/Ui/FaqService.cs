using SmsHubNext.Shared.Ui;

namespace SmsHubNext.Features.Landing.Ui;

public sealed class FaqService(UiOperations operations, FaqHandler handler)
{
    public Task<UiResult<IReadOnlyList<FaqItem>>> List(CancellationToken token) => operations.Run((identity, ct) => handler.List(ct), token);
    public Task<UiResult<string?>> Save(int? id, SaveFaqRequest request, CancellationToken token) => operations.Run((identity, ct) => handler.Save(id, request, identity.ApiKeyId, ct), token);
    public Task<UiResult<string?>> Delete(int id, CancellationToken token) => operations.Run((identity, ct) => handler.Delete(id, ct), token);
    public Task<UiResult<string?>> Move(int id, int direction, CancellationToken token) => operations.Run((identity, ct) => handler.Move(id, direction, identity.ApiKeyId, ct), token);
}
