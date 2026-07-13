using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.SenderLines;

public sealed class UpdateSenderLineRequest
{
    public string LineNumber { get; init; } = string.Empty;
    public bool IsSharedLine { get; init; }
    public short? CustomerId { get; init; }
    public int? ProviderAccountId { get; init; }
    public bool IsActive { get; init; }

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(LineNumber))
            return Error.Validation("sender_lines.line_number_required", UserMessages.ReferenceData.LineNumberRequired);

        if (CustomerId <= 0)
            return Error.Validation("sender_lines.invalid_customer", UserMessages.ReferenceData.InvalidCustomer);

        if (IsSharedLine && CustomerId is not null)
            return Error.Validation("sender_lines.shared_line_has_owner", UserMessages.ReferenceData.SharedLineHasOwner);

        if (ProviderAccountId <= 0)
            return Error.Validation("sender_lines.invalid_provider_account", UserMessages.ReferenceData.InvalidProviderAccount);

        return Result.Success();
    }
}
