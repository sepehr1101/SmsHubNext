using SmsHubNext.Shared.Ui;
using QuoteRequest = SmsHubNext.Features.Tariffs.QuoteRequest;
using CostQuote = SmsHubNext.Features.Tariffs.CostQuote;
using SendMessagesRequest = SmsHubNext.Features.Sending.SendMessagesRequest;
using SendMessagesResponse = SmsHubNext.Features.Sending.SendMessagesResponse;
using SmsHubNext.Features.Tariffs;

namespace SmsHubNext.Features.Sending.Ui;

public class SendingService(
    UiOperations operations,
    QuoteHandler quoteHandler,
    SendMessagesHandler sendMessagesHandler)
{
    public virtual Task<UiResult<CostQuote>> Quote(QuoteRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => quoteHandler.Handle(request, cancellationToken), token);

    public virtual Task<UiResult<SendMessagesResponse>> Send(SendMessagesRequest request, short customerId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => sendMessagesHandler.HandleManagement(request, identity, customerId, cancellationToken), token);

}
