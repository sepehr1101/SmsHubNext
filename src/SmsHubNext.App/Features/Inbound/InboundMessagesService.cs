using SmsHubNext.App.Shared.Api;

namespace SmsHubNext.App.Features.Inbound;

public sealed class InboundMessagesService
{
    private readonly ManagementApiClient _api;
    public InboundMessagesService(ManagementApiClient api) => _api = api;
    public Task<ApiClientResult<IReadOnlyList<InboundMessageItem>>> List(string? recipient, int take, CancellationToken token)
    {
        string filter = string.IsNullOrWhiteSpace(recipient) ? string.Empty : $"&recipientNumber={Uri.EscapeDataString(recipient.Trim())}";
        return _api.Get<IReadOnlyList<InboundMessageItem>>($"inbound-messages?take={take}{filter}", token);
    }
}
