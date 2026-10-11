using SmsHubNext.App.Shared.Api;

namespace SmsHubNext.App.Features.Sending;

public sealed class SendingService
{
    private readonly ManagementApiClient _api;
    public SendingService(ManagementApiClient api) => _api = api;
    public Task<ApiClientResult<CostQuote>> Quote(QuoteRequest request, CancellationToken token) => _api.Post<QuoteRequest, CostQuote>("tariffs/quote", request, token);
    public Task<ApiClientResult<SendMessagesResponse>> Send(SendMessagesRequest request, CancellationToken token) => _api.PostWithCustomerApiKey<SendMessagesRequest, SendMessagesResponse>("messages", request, token);
}
