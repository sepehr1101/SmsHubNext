using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.SenderLines;

public sealed class AssignProviderAccountRequest
{
    public int? ProviderAccountId { get; init; }

    public Result Validate()
    {
        if (ProviderAccountId <= 0)
            return Error.Validation("sender_lines.invalid_provider_account", UserMessages.ReferenceData.InvalidProviderAccount);

        return Result.Success();
    }
}
