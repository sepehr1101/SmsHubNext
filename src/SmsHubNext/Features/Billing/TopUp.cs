using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Billing;

public sealed class TopUpRequest
{
    public short CustomerId { get; init; }
    public decimal Amount { get; init; }
    public string? Reference { get; init; }

    public Result Validate()
    {
        if (CustomerId <= 0)
            return Error.Validation("balances.customer_required", UserMessages.Balances.CustomerRequired);

        if (Amount <= 0)
            return Error.Validation("balances.amount_positive", UserMessages.Balances.AmountPositive);

        if (Reference?.Length > 100)
            return Error.Validation("balances.reference_too_long", UserMessages.Balances.ReferenceTooLong);

        return Result.Success();
    }
}

public sealed record TopUpResponse(short CustomerId, decimal Balance, bool IsDuplicate = false);
