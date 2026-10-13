using SmsHubNext.Shared.Ui;
using InboundMessageItem = SmsHubNext.Features.Inbound.InboundMessage;

namespace SmsHubNext.Features.Inbound.Ui;

public sealed class InboundMessagesService(
    UiOperations operations,
    ListInboundMessagesHandler listInboundMessagesHandler)
{
    public Task<UiResult<IReadOnlyList<InboundMessageItem>>> List(string? recipient, int take, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listInboundMessagesHandler.Handle(recipient?.Trim(), take, cancellationToken), token);

}
