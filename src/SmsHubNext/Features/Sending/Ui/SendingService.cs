using SmsHubNext.Shared.Ui;
using QuoteRequest = SmsHubNext.Features.Tariffs.QuoteRequest;
using CostQuote = SmsHubNext.Features.Tariffs.CostQuote;
using SendMessagesRequest = SmsHubNext.Features.Sending.SendMessagesRequest;
using SendMessagesResponse = SmsHubNext.Features.Sending.SendMessagesResponse;
using SmsHubNext.Features.Tariffs;

namespace SmsHubNext.Features.Sending.Ui;

public sealed class SendingService(
    UiOperations operations,
    QuoteHandler quoteHandler,
    SendMessagesHandler sendMessagesHandler)
{
    public Task<UiResult<CostQuote>> Quote(QuoteRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => quoteHandler.Handle(request, cancellationToken), token);

    public Task<UiResult<SendMessagesResponse>> Send(SendMessagesRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => sendMessagesHandler.Handle(request, identity, cancellationToken), token, customerKey: true);

}
