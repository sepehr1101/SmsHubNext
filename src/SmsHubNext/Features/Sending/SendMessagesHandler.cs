using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Sending;

/// <summary>
/// Accepts a send request.
///
/// WALKING SKELETON — for now it only validates and acknowledges. The real path
/// (debit balance, persist <c>MessageBatch</c> + <c>Message</c> rows as Queued,
/// hand off to background dispatch, send via Magfa) is added in later increments
/// (persistence: Phase 2, provider: Phase 1).
/// </summary>
public sealed class SendMessagesHandler
{
    public Result<SendMessagesResponse> Handle(SendMessagesRequest request)
    {
        var validation = request.Validate();
        if (validation.IsFailure)
            return validation.Error!;

        var response = new SendMessagesResponse(Guid.NewGuid(), request.Messages.Count);
        return response;
    }
}
