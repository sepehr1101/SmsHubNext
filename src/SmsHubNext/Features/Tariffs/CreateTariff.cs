using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Sms;

namespace SmsHubNext.Features.Tariffs;

public sealed class CreateTariffRequest
{
    public byte ProviderId { get; init; }
    public byte? MessageTypeId { get; init; }
    public SmsEncoding Encoding { get; init; }
    public DateTime EffectiveFromUtc { get; init; }
    public DateTime? EffectiveToUtc { get; init; }
    public bool IsActive { get; init; } = true;
    public IReadOnlyList<CreateTariffRateRequest> Rates { get; init; } = Array.Empty<CreateTariffRateRequest>();

    public Result Validate()
    {
        if (ProviderId == 0)
            return Error.Validation("tariffs.provider_required", UserMessages.Tariffs.ProviderRequired);

        if (!Enum.IsDefined(Encoding))
            return Error.Validation("tariffs.encoding_invalid", UserMessages.Tariffs.EncodingInvalid);

        if (EffectiveFromUtc == default)
            return Error.Validation("tariffs.effective_from_required", UserMessages.Tariffs.EffectiveFromRequired);

        if (EffectiveToUtc is not null && EffectiveToUtc <= EffectiveFromUtc)
            return Error.Validation("tariffs.effective_range_invalid", UserMessages.Tariffs.EffectiveRangeInvalid);

        return ValidateRates();
    }

    private Result ValidateRates()
    {
        if (Rates.Count == 0)
            return Error.Validation("tariffs.rates_required", UserMessages.Tariffs.RatesRequired);

        List<CreateTariffRateRequest> orderedRates = Rates.OrderBy(rate => rate.MinChars).ToList();
        int expectedMinChars = 1;

        for (int index = 0; index < orderedRates.Count; index++)
        {
            CreateTariffRateRequest rate = orderedRates[index];
            if (rate.MinChars != expectedMinChars || rate.MinChars <= 0)
                return Error.Validation("tariffs.rate_ranges_invalid", UserMessages.Tariffs.RateRangesInvalid);

            if (rate.PricePerSegment <= 0)
                return Error.Validation("tariffs.rate_price_invalid", UserMessages.Tariffs.RatePriceInvalid);

            bool isLast = index == orderedRates.Count - 1;
            if (rate.MaxChars is null)
            {
                if (!isLast)
                    return Error.Validation("tariffs.rate_ranges_invalid", UserMessages.Tariffs.RateRangesInvalid);

                continue;
            }

            if (rate.MaxChars < rate.MinChars || isLast)
                return Error.Validation("tariffs.rate_ranges_invalid", UserMessages.Tariffs.RateRangesInvalid);

            expectedMinChars = rate.MaxChars.Value + 1;
        }

        return orderedRates[^1].MaxChars is null
            ? Result.Success()
            : Error.Validation("tariffs.rate_ranges_invalid", UserMessages.Tariffs.RateRangesInvalid);
    }
}

public sealed class CreateTariffRateRequest
{
    public short MinChars { get; init; }
    public short? MaxChars { get; init; }
    public decimal PricePerSegment { get; init; }
}

public sealed record CreateTariffResponse(int Id);
