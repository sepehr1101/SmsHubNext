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
            return Error.Validation("balances.customer_required", "A valid customer id is required.");

        if (Amount <= 0)
            return Error.Validation("balances.amount_positive", "Top-up amount must be positive.");

        return Result.Success();
    }
}

public sealed record TopUpResponse(short CustomerId, decimal Balance);
