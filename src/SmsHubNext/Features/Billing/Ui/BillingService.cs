using SmsHubNext.Shared.Ui;
using CustomerBalanceItem = SmsHubNext.Features.Billing.CustomerBalance;
using BalanceTransactionItem = SmsHubNext.Features.Billing.BalanceTransaction;
using TopUpRequest = SmsHubNext.Features.Billing.TopUpRequest;
using TopUpResponse = SmsHubNext.Features.Billing.TopUpResponse;

namespace SmsHubNext.Features.Billing.Ui;

public sealed class BillingService(
    UiOperations operations,
    GetBalanceHandler getBalanceHandler,
    ListTransactionsHandler listTransactionsHandler,
    TopUpHandler topUpHandler)
{
    public Task<UiResult<CustomerBalanceItem>> GetBalance(short customerId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => getBalanceHandler.Handle(customerId, cancellationToken), token);

    public Task<UiResult<IReadOnlyList<BalanceTransactionItem>>> ListTransactions(short customerId, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => listTransactionsHandler.Handle(customerId, cancellationToken), token);

    public Task<UiResult<TopUpResponse>> TopUp(TopUpRequest request, CancellationToken token) =>
        operations.Run((identity, cancellationToken) => topUpHandler.Handle(request, cancellationToken), token);

}
