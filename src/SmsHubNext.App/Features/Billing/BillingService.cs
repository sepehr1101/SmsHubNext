using SmsHubNext.App.Shared.Api;
namespace SmsHubNext.App.Features.Billing;
public sealed class BillingService
{
    private readonly ManagementApiClient _api; public BillingService(ManagementApiClient api) => _api = api;
    public Task<ApiClientResult<CustomerBalanceItem>> GetBalance(short customerId, CancellationToken token) => _api.Get<CustomerBalanceItem>($"balances?customerId={customerId}", token);
    public Task<ApiClientResult<IReadOnlyList<BalanceTransactionItem>>> ListTransactions(short customerId, CancellationToken token) => _api.Get<IReadOnlyList<BalanceTransactionItem>>($"balances/transactions?customerId={customerId}", token);
    public Task<ApiClientResult<TopUpResponse>> TopUp(TopUpRequest request, CancellationToken token) => _api.Post<TopUpRequest, TopUpResponse>("balances/top-up", request, token);
}
