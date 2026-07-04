using SmsHubNext.Shared.Enums;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.DispatchOperations;

/// <summary>Operational filters for dispatch queue reports.</summary>
public sealed class DispatchOperationsRequest
{
    public string? FromJalali { get; init; }
    public string? ToJalali { get; init; }
    public short? CustomerId { get; init; }
    public byte? ProviderId { get; init; }
    public BatchStatus? Status { get; init; }
    public bool OnlyProblems { get; init; }
    public int Page { get; init; } = 1;
    public int Take { get; init; } = 50;

    public int Offset => (Page - 1) * Take;

    public Result Validate(bool includePaging)
    {
        if (FromJalali is not null && !IsValidJalaliDate(FromJalali))
            return Error.Validation("dispatch_operations.from_jalali_invalid", UserMessages.DispatchOperations.FromJalaliInvalid);

        if (ToJalali is not null && !IsValidJalaliDate(ToJalali))
            return Error.Validation("dispatch_operations.to_jalali_invalid", UserMessages.DispatchOperations.ToJalaliInvalid);

        if (FromJalali is not null && ToJalali is not null && string.CompareOrdinal(FromJalali, ToJalali) > 0)
            return Error.Validation("dispatch_operations.invalid_range", UserMessages.DispatchOperations.InvalidRange);

        if (CustomerId <= 0)
            return Error.Validation("dispatch_operations.customer_invalid", UserMessages.DispatchOperations.CustomerInvalid);

        if (ProviderId == 0)
            return Error.Validation("dispatch_operations.provider_invalid", UserMessages.DispatchOperations.ProviderInvalid);

        if (includePaging && Page <= 0)
            return Error.Validation("dispatch_operations.page_invalid", UserMessages.DispatchOperations.PageInvalid);

        if (includePaging && (Take <= 0 || Take > 200))
            return Error.Validation("dispatch_operations.take_invalid", UserMessages.DispatchOperations.TakeInvalid);

        return Result.Success();
    }

    private static bool IsValidJalaliDate(string value)
    {
        if (value.Length != 10)
            return false;

        return char.IsDigit(value[0])
            && char.IsDigit(value[1])
            && char.IsDigit(value[2])
            && char.IsDigit(value[3])
            && value[4] == '/'
            && char.IsDigit(value[5])
            && char.IsDigit(value[6])
            && value[7] == '/'
            && char.IsDigit(value[8])
            && char.IsDigit(value[9]);
    }
}
