using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Customers;

public sealed class CreateCustomerRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("customers.name_required", "A customer name is required.");

        if (string.IsNullOrWhiteSpace(Code))
            return Error.Validation("customers.code_required", "A customer code is required.");

        return Result.Success();
    }
}

public sealed record CreateCustomerResponse(short Id);
