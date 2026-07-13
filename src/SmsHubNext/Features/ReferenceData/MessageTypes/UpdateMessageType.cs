using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData.MessageTypes;

public sealed class UpdateMessageTypeRequest
{
    public string Name { get; init; } = string.Empty;
    public bool IsActive { get; init; }

    public Result Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("message_types.name_required", UserMessages.ReferenceData.NameRequired);

        return Result.Success();
    }
}
