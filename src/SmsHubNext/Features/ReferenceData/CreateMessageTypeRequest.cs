using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.ReferenceData;

public sealed class CreateMessageTypeRequest
{
    /// <summary>
    /// The stable classification key (README §4.6, locked decision #4). It is not auto-assigned:
    /// the fact table denormalizes <c>MessageTypeId</c> and reports group by it, so the admin chooses
    /// a stable <c>TINYINT</c> (1–255) that does not change once messages reference it.
    /// </summary>
    public byte Id { get; init; }

    /// <summary>Human-readable name (Persian allowed), e.g. <c>OTP</c>.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Stable machine code (ASCII, unique), e.g. <c>otp</c>.</summary>
    public string Code { get; init; } = string.Empty;

    public Result Validate()
    {
        if (Id == 0)
            return Error.Validation("message_types.id_required", "A non-zero message type id is required.");

        if (string.IsNullOrWhiteSpace(Name))
            return Error.Validation("message_types.name_required", "A name is required.");

        if (string.IsNullOrWhiteSpace(Code))
            return Error.Validation("message_types.code_required", "A code is required.");

        return Result.Success();
    }
}
