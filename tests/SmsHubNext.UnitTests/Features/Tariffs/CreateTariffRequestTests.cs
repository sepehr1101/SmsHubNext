using SmsHubNext.Features.Tariffs;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Sms;
using Xunit;

namespace SmsHubNext.UnitTests.Features.Tariffs;

public sealed class CreateTariffRequestTests
{
    [Fact]
    public void Accepts_contiguous_rates_ending_in_an_unbounded_band()
    {
        CreateTariffRequest request = ValidRequest(
            new CreateTariffRateRequest { MinChars = 1, MaxChars = 160, PricePerSegment = 1000m },
            new CreateTariffRateRequest { MinChars = 161, MaxChars = null, PricePerSegment = 2000m });

        Assert.True(request.Validate().IsSuccess);
    }

    [Fact]
    public void Rejects_a_gap_between_rate_ranges()
    {
        CreateTariffRequest request = ValidRequest(
            new CreateTariffRateRequest { MinChars = 1, MaxChars = 160, PricePerSegment = 1000m },
            new CreateTariffRateRequest { MinChars = 162, MaxChars = null, PricePerSegment = 2000m });

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("tariffs.rate_ranges_invalid", result.Error!.Code);
    }

    [Fact]
    public void Rejects_a_non_positive_price()
    {
        CreateTariffRequest request = ValidRequest(
            new CreateTariffRateRequest { MinChars = 1, MaxChars = null, PricePerSegment = 0m });

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("tariffs.rate_price_invalid", result.Error!.Code);
    }

    [Fact]
    public void Rejects_an_effective_to_before_effective_from()
    {
        DateTime effectiveFrom = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        CreateTariffRequest request = new CreateTariffRequest
        {
            ProviderId = 1,
            Encoding = SmsEncoding.Gsm7,
            EffectiveFromUtc = effectiveFrom,
            EffectiveToUtc = effectiveFrom.AddSeconds(-1),
            Rates = new[]
            {
                new CreateTariffRateRequest { MinChars = 1, MaxChars = null, PricePerSegment = 1000m },
            },
        };

        Result result = request.Validate();

        Assert.True(result.IsFailure);
        Assert.Equal("tariffs.effective_range_invalid", result.Error!.Code);
    }

    private static CreateTariffRequest ValidRequest(params CreateTariffRateRequest[] rates) => new()
    {
        ProviderId = 1,
        Encoding = SmsEncoding.Gsm7,
        EffectiveFromUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Rates = rates,
    };
}
