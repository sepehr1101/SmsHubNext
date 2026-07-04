using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.Customers;

public sealed class CreateCustomerRequest
{
    public string Name { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("customers.name_required", UserMessages.ReferenceData.CustomerNameRequired);

        if (string.IsNullOrWhiteSpace(Code))
            return Error.Validation("customers.code_required", UserMessages.ReferenceData.CustomerCodeRequired);

        return Result.Success();
    }
}

public sealed record CreateCustomerResponse(short Id);
