using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Tariffs;

public sealed class UpdateTariffRequest
{
    public DateTime? EffectiveToUtc { get; init; }
    public bool IsActive { get; init; }

    public Result Validate(DateTime effectiveFromUtc)
    {
        if (EffectiveToUtc is not null && EffectiveToUtc <= effectiveFromUtc)
            return Error.Validation("tariffs.effective_range_invalid", UserMessages.Tariffs.EffectiveRangeInvalid);

        return Result.Success();
    }
}
